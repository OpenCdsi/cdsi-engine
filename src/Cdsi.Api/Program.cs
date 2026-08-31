/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Api;
using Cdsi.Contracts;
using Cdsi.Core.Models;
using Cdsi.Core.Pipeline;
using Cdsi.Core.ReferenceData;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Microsoft.OpenApi.Models.OpenApiInfo (correct for Swashbuckle 6.x/Microsoft.OpenApi 1.x)
    // was replaced by Microsoft.OpenApi.OpenApiInfo when this project moved to Swashbuckle
    // 10.2.3/Microsoft.OpenApi 2.x alongside the net8.0 -> net10.0 upgrade - the old
    // Microsoft.OpenApi.Models namespace no longer exists in the v2.x line at all. Caught by
    // checking Swashbuckle's own official v10 migration guide before trusting the version bump
    // alone would be enough - it explicitly wasn't.
    options.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
    {
        Title = "CDSi Immunization Engine API",
        Version = "v1",
        Description = "Real-time immunization forecasting per the CDC's CDSi Logic Specification v4.6."
    });
});

// The reference data catalog (30 antigen files + Schedule) is expensive XML parsing - loaded
// ONCE at startup as a singleton, not per-request. Data path resolution mirrors Cdsi.Demo's own
// FindDataDirectory pattern: CDSI_DATA_PATH (set by the Dockerfile/docker-compose to /data, the
// mounted volume - see README on why reference data is deliberately NOT baked into the image)
// wins if set; otherwise walk up from the executable's location looking for Cdsi.sln, for a
// working `dotnet run` from within a repo checkout with no environment variable needed at all.
var dataRoot = builder.Configuration["CDSI_DATA_PATH"] ?? FindDataDirectory();
builder.Services.AddSingleton(_ =>
{
    var antigensPath = Path.Combine(dataRoot, "antigens");
    var schedulePath = Path.Combine(dataRoot, "schedule", "ScheduleSupportingData.xml");
    return ReferenceDataRepository.Load(antigensPath, schedulePath);
});

var app = builder.Build();

// Swagger UI/JSON are only exposed outside Production - this is a clinical data API, and
// leaving interactive API docs always reachable means anyone who can hit this port can browse
// the full API surface and fire test requests at it. AddSwaggerGen/AddEndpointsApiExplorer
// above stay registered unconditionally (cheap DI registrations, no endpoints exposed by
// themselves) - only the middleware that actually serves /swagger is gated here.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();

    // WORKAROUND for a real, confirmed upstream bug, not a guess: Swashbuckle.AspNetCore 10.2.3
    // (via its Microsoft.OpenApi 2.x dependency) emits "openapi": "3.0.4" - a genuinely valid
    // OpenAPI 3.0 document, but the swagger-ui version this Swashbuckle release bundles has its
    // own version-string regex that only recognizes patch versions 0-3 (fixed upstream in
    // swagger-ui 5.19.0, not yet pulled into this Swashbuckle release - see
    // domaindrivendev/Swashbuckle.AspNetCore#3265 and swagger-api/swagger-ui#10502). The result
    // is Swagger UI's own "does not specify a valid version field" error on an otherwise-correct
    // document - confirmed directly: fetching /swagger/v1/swagger.json returns a well-formed
    // document with a real, valid "openapi" field right at the top, contradicting the UI's error.
    //
    // Patches only the version digits themselves (3.0.4 -> 3.0.1, both 5 characters) via a
    // capturing-group replacement that leaves the surrounding "openapi": "..." structure and
    // whitespace completely untouched - guarantees an identical byte length to the original, so
    // there's no Content-Length mismatch to account for. 3.0.1 through 3.0.4 are editorial-only
    // revisions of the same OpenAPI 3.0 spec (no schema/feature differences), so this only
    // changes the label Swagger UI reads, not the actual document Swagger UI renders.
    //
    // This is deliberately NOT a Microsoft.OpenApi object-model fix (e.g. a PreSerializeFilter
    // setting some OpenApiVersion-style property) - that API surface exists in principle, but I
    // couldn't confirm its exact shape for this specific package version without a real build to
    // test against, and a wrong guess there would silently do nothing. A raw string patch on the
    // already-serialized response depends on nothing but the confirmed byte content of the
    // problem itself.
    app.Use(async (context, next) =>
    {
        if (!context.Request.Path.StartsWithSegments("/swagger") || !context.Request.Path.Value!.EndsWith("swagger.json"))
        {
            await next();
            return;
        }

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await next();
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        buffer.Seek(0, SeekOrigin.Begin);
        var content = await new StreamReader(buffer).ReadToEndAsync();
        // Capturing groups + backreferences, not lookbehind/lookahead - a variable-width
        // lookbehind (needed here, since \s* between the colon and quote isn't fixed-width) isn't
        // supported by every regex engine, and this couldn't be verified against a real .NET
        // build - capturing groups are universally supported and were verified directly (Python's
        // re, same semantics for this construct) against both compact and formatted JSON before
        // trusting this.
        var patched = System.Text.RegularExpressions.Regex.Replace(
            content, @"(""openapi""\s*:\s*"")3\.0\.4("")", "${1}3.0.1${2}");

        await context.Response.WriteAsync(patched);
    });

    app.UseSwaggerUI();
}

// Unexpected exceptions get logged in full server-side but never leak details to the caller -
// this is a clinical data API, not a place to return stack traces. InvalidRequestException
// (a well-formed-JSON-but-invalid-content problem, like an unrecognized gender string) is
// distinguished from a genuine bug and gets its own message and 400 status; anything else
// becomes a generic 500.
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        var exception = feature?.Error;

        if (exception is InvalidRequestException invalidRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await Results.Problem(detail: invalidRequest.Message, statusCode: StatusCodes.Status400BadRequest, title: "Invalid request")
                .ExecuteAsync(context);
            return;
        }

        // Malformed/incomplete request bodies (missing required JSON properties, invalid JSON
        // syntax, etc.) surface from ASP.NET Core's own minimal-API body-binding infrastructure
        // as BadHttpRequestException, which carries the CORRECT status code (400) on the
        // exception itself - this branch respects that instead of discarding it and falling
        // through to the generic 500 below. Confirmed necessary by a real test run: without
        // this, a request missing the required `dateOfBirth` field surfaced as a 500, not the
        // 400 a malformed client request should actually return. The inner JsonException's own
        // message ("was missing required properties, including the following: dateOfBirth") is
        // preferred when present - it's the specific, actionable detail; the outer
        // BadHttpRequestException's own message is a more generic "failed to read parameter."
        if (exception is Microsoft.AspNetCore.Http.BadHttpRequestException badRequest)
        {
            var detail = badRequest.InnerException?.Message ?? badRequest.Message;
            context.Response.StatusCode = badRequest.StatusCode;
            await Results.Problem(detail: detail, statusCode: badRequest.StatusCode, title: "Invalid request")
                .ExecuteAsync(context);
            return;
        }

        context.RequestServices.GetRequiredService<ILogger<Program>>()
            .LogError(exception, "Unhandled exception processing {Method} {Path}", context.Request.Method, context.Request.Path);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await Results.Problem(detail: "An unexpected error occurred.", statusCode: StatusCodes.Status500InternalServerError, title: "Internal server error")
            .ExecuteAsync(context);
    });
});

// Resolve the singleton once at startup (not lazily on first request) so a bad data path fails
// fast with a clear startup error, rather than surfacing as a confusing 500 on the first real
// request an EHR integration sends.
var referenceData = app.Services.GetRequiredService<ReferenceDataRepository>();
app.Logger.LogInformation(
    "Loaded reference data from {DataRoot}: {SeriesCount} series across {AntigenCount} antigens, {VaccineGroupCount} vaccine groups.",
    dataRoot, referenceData.AllSeries.Count, referenceData.AllSeries.Select(s => s.Antigen).Distinct().Count(), referenceData.VaccineGroups.Count);

app.MapGet("/health", (ReferenceDataRepository data) => Results.Ok(new
{
    status = "healthy",
    dataLoaded = new
    {
        seriesCount = data.AllSeries.Count,
        antigenCount = data.AllSeries.Select(s => s.Antigen).Distinct().Count(),
        vaccineGroupCount = data.VaccineGroups.Count
    }
}))
.WithName("HealthCheck");

app.MapPost("/api/v1/forecast", (ForecastRequestDto request, ReferenceDataRepository data) =>
{
    var patient = RequestMapping.ToPatient(request);
    var doses = RequestMapping.ToAdministeredDoses(request);
    var assessmentDate = RequestMapping.ResolveAssessmentDate(request, DateOnly.FromDateTime(DateTime.UtcNow));

    var results = GeneratePatientForecast.Execute(
        patient, doses, data.AllSeries, data.Schedule, data.VaccineGroups,
        data.ImmunityByAntigen, data.ContraindicationsByAntigen, assessmentDate);

    return Results.Ok(ResponseMapping.ToResponse(request.PatientId, assessmentDate, results));
})
.WithName("GenerateForecast");

// Reference-data browsing endpoints (GET /api/v2/antigens|vaccines|vaccines/groups|observations/*)
// - mirrors the shape of an existing NodeJS "CDSi Supporting Data API" this project is
// replicating. Registered on a shared route group so QFieldsEndpointFilter (the q/fields query
// parameters from that API's own spec) applies uniformly to all of them, and to any future
// /api/v2 endpoint, without needing to be wired in by hand at each individual MapGet call.
var referenceDataApi = app.MapGroup("/api/v2").AddEndpointFilter<QFieldsEndpointFilter>();
referenceDataApi.MapAntigenEndpoints();
referenceDataApi.MapVaccineEndpoints();
referenceDataApi.MapVaccineGroupEndpoints();
referenceDataApi.MapObservationEndpoints();

app.Run();

static string FindDataDirectory()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Cdsi.sln")))
    {
        dir = dir.Parent;
    }
    if (dir is null)
    {
        throw new InvalidOperationException(
            "Couldn't find the repo root (Cdsi.sln) walking up from the executable's directory, " +
            "and CDSI_DATA_PATH was not set. Set the CDSI_DATA_PATH environment variable, " +
            "or run this from within the cdsi-engine checkout.");
    }
    return Path.Combine(dir.FullName, "data");
}

/// <summary>Makes the top-level-statements Program class accessible to Cdsi.Api.Tests' WebApplicationFactory&lt;Program&gt;, which otherwise can't see the implicitly-internal generated class.</summary>
public partial class Program { }
