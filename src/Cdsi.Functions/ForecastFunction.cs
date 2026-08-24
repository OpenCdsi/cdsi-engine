/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using System.Text.Json;
using Cdsi.Contracts;
using Cdsi.Core.Pipeline;
using Cdsi.Core.ReferenceData;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Cdsi.Functions;

/// <summary>
/// The same forecast capability Cdsi.Api exposes over HTTP, as a second API surface. Reuses
/// Cdsi.Contracts' exact same request/response DTOs and mapping - no duplicated shape logic
/// between the two API surfaces.
///
/// AuthorizationLevel.Function requires a function or host key on every request by default -
/// Azure Functions' own built-in access control, worth knowing given the earlier conversation
/// about adding API key auth to Cdsi.Api: Functions gets a form of this for free, Cdsi.Api
/// currently doesn't have anything equivalent.
/// </summary>
public class ForecastFunction(ReferenceDataRepository data, ILogger<ForecastFunction> logger)
{
    [Function("GenerateForecast")]
    public async Task<IActionResult> GenerateForecast(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "api/v1/forecast")] HttpRequest req)
    {
        ForecastRequestDto? request;
        try
        {
            request = await req.ReadFromJsonAsync<ForecastRequestDto>();
        }
        catch (JsonException ex)
        {
            // Same failure mode Cdsi.Api's own BadHttpRequestException handling covers (missing
            // required properties, malformed JSON) - ReadFromJsonAsync raises the JsonException
            // directly here rather than ASP.NET Core minimal API's own BadHttpRequestException
            // wrapper, since that wrapping is specific to minimal API's RequestDelegateFactory,
            // not something this extension method does on its own. Not verified against real
            // execution - this sandbox can't run Functions locally to confirm - flagged as the
            // same kind of honest gap as everywhere else in this project.
            return new BadRequestObjectResult(new { title = "Invalid request", detail = ex.Message });
        }

        if (request is null)
        {
            return new BadRequestObjectResult(new { title = "Invalid request", detail = "Request body is required." });
        }

        try
        {
            var patient = RequestMapping.ToPatient(request);
            var doses = RequestMapping.ToAdministeredDoses(request);
            var assessmentDate = RequestMapping.ResolveAssessmentDate(request, DateOnly.FromDateTime(DateTime.UtcNow));

            var results = GeneratePatientForecast.Execute(
                patient, doses, data.AllSeries, data.Schedule, data.VaccineGroups,
                data.ImmunityByAntigen, data.ContraindicationsByAntigen, assessmentDate);

            return new OkObjectResult(ResponseMapping.ToResponse(request.PatientId, assessmentDate, results));
        }
        catch (InvalidRequestException ex)
        {
            return new BadRequestObjectResult(new { title = "Invalid request", detail = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception processing GenerateForecast");
            return new ObjectResult(new { title = "Internal server error", detail = "An unexpected error occurred." })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }

    [Function("Health")]
    public IActionResult Health(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequest req)
    {
        // Anonymous, unlike the forecast function - a health/liveness check should be reachable
        // without a key, same reasoning Cdsi.Api's own /health endpoint follows.
        return new OkObjectResult(new
        {
            status = "healthy",
            dataLoaded = new
            {
                seriesCount = data.AllSeries.Count,
                antigenCount = data.AllSeries.Select(s => s.Antigen).Distinct().Count(),
                vaccineGroupCount = data.VaccineGroups.Count
            }
        });
    }
}
