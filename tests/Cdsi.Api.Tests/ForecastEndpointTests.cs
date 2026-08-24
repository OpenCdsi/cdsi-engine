/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Cdsi.Api.Tests;

/// <summary>
/// Real, end-to-end HTTP tests against the actual API, via WebApplicationFactory&lt;Program&gt; -
/// the real Program.cs startup runs in-memory, including real data loading from the real repo
/// data/ directory (via Program.cs's own FindDataDirectory, walking up from the test host's own
/// bin/ output directory to the real Cdsi.sln - the same proven mechanism Cdsi.Demo already
/// uses). Not mocked at any layer - this is the real GeneratePatientForecast pipeline reached
/// through the real HTTP/JSON boundary.
/// </summary>
public class ForecastEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ForecastEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Health_ReturnsHealthyWithRealLoadedDataCounts()
    {
        var response = await _client.GetAsync("/health");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("healthy", body.GetProperty("status").GetString());
        // Real data: 30 antigen files - matches every prior real run of this project (the demo,
        // and every full-catalog test), not a guessed number.
        Assert.Equal(30, body.GetProperty("dataLoaded").GetProperty("antigenCount").GetInt32());
    }

    [Fact]
    public async Task Forecast_RealNewbornZeroDoses_ReturnsRealVaccineGroupForecasts()
    {
        // Mirrors the exact "newborn, no doses, no risk indications" scenario already hand-traced
        // and verified precisely in HepBFullCatalogCompetitionTests and GeneratePatientForecastTests -
        // reused here specifically because its outcome is already known with high confidence, not
        // because it's the most "interesting" possible request.
        var request = new
        {
            patientId = "test-patient-1",
            dateOfBirth = "2024-01-01",
            assessmentDate = "2024-01-01",
            administeredDoses = Array.Empty<object>()
        };

        var response = await _client.PostAsJsonAsync("/api/v1/forecast", request);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        Assert.Equal("test-patient-1", body.GetProperty("patientId").GetString());
        var forecasts = body.GetProperty("vaccineGroupForecasts");
        Assert.True(forecasts.GetArrayLength() > 0);

        var hepB = forecasts.EnumerateArray().Single(f => f.GetProperty("vaccineGroupName").GetString() == "HepB");
        Assert.Equal("NotComplete", hepB.GetProperty("status").GetString());
        Assert.True(hepB.GetProperty("shouldForecast").GetBoolean());
        Assert.Equal("SingleAntigen", hepB.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Forecast_RealHepBTwoDosesGiven_ShowsInProcessForecastForOneOfTheGenuineCandidates()
    {
        // Same real HepB fixture (CVX "08" at DOB and DOB+28 days) as
        // HepBFullCatalogCompetitionTests' own two-dose scenario - deliberately reused rather
        // than reusing GeneratePatientSeriesForecastTests' scoped single-series fixture, because
        // THIS request goes through the real, full, unscoped 18-series catalog. That test
        // already established the winner must be one of three genuine candidates ("HepB 3-dose
        // series", "HepB 4-dose series", or "HepB Heplisav-B secondary 4-dose series") without
        // pinning an exact winner - the first two would both show ForecastDoseNumber 3, but the
        // third (only Dose 1 satisfiable by a 2-month-old) would show 2. Asserting exactly 3
        // here would be an unverified guess for the same reason flagged there - checked instead
        // for what's actually knowable: an in-process forecast exists, for a real, expected dose
        // number.
        var request = new
        {
            patientId = "test-patient-2",
            dateOfBirth = "2020-01-01",
            assessmentDate = "2020-09-01",
            administeredDoses = new[]
            {
                new { doseId = "d1", cvx = "08", dateAdministered = "2020-01-01" },
                new { doseId = "d2", cvx = "08", dateAdministered = "2020-03-01" }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/v1/forecast", request);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        var forecasts = body.GetProperty("vaccineGroupForecasts");
        var hepB = forecasts.EnumerateArray().Single(f => f.GetProperty("vaccineGroupName").GetString() == "HepB");

        Assert.Equal("NotComplete", hepB.GetProperty("status").GetString());
        Assert.True(hepB.GetProperty("shouldForecast").GetBoolean());
        var doseNumber = hepB.GetProperty("forecastDoseNumber").GetInt32();
        Assert.True(doseNumber is 2 or 3, $"Expected dose 2 or 3 depending on which genuine candidate wins, got {doseNumber}.");
    }

    [Fact]
    public async Task Forecast_InvalidGender_Returns400WithClearMessage()
    {
        var request = new
        {
            patientId = "test-patient-3",
            dateOfBirth = "2020-01-01",
            gender = "not-a-real-gender"
        };

        var response = await _client.PostAsJsonAsync("/api/v1/forecast", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("gender", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Forecast_MissingRequiredDateOfBirth_Returns400()
    {
        // No dateOfBirth at all - System.Text.Json's own `required` member enforcement, wired
        // through minimal API model binding, should reject this before the handler ever runs.
        var request = new { patientId = "test-patient-4" };

        var response = await _client.PostAsJsonAsync("/api/v1/forecast", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Forecast_DefaultsAssessmentDateToToday_WhenOmitted()
    {
        var request = new
        {
            patientId = "test-patient-5",
            dateOfBirth = "2024-01-01"
        };

        var response = await _client.PostAsJsonAsync("/api/v1/forecast", request);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        var assessmentDate = DateOnly.Parse(body.GetProperty("assessmentDate").GetString()!);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), assessmentDate);
    }
}
