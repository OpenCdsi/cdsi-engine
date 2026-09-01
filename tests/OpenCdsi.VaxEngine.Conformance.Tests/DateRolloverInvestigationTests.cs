/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Core.Models;
using OpenCdsi.VaxEngine.Core.Pipeline;
using Xunit;

namespace OpenCdsi.VaxEngine.Conformance.Tests;

/// <summary>
/// Real verification for the CALCDT-5 month-rollover fix in DurationExpression.AddMonthsWithRollover
/// (see its own doc comment for the full derivation - a spec-confirmed bug in core, shared date
/// arithmetic, not specific to DTaP: .NET's DateOnly.AddMonths clamps when the source day-of-month
/// doesn't exist in the target month, but CALCDT-5 explicitly requires rolling forward to the 1st
/// of the following month instead). Found via three real corpus cases sharing the identical
/// pattern - 2013-0003 (DTaP), 2013-0130 (Pediarix), 2013-0165 (Pentacel) - all DOB 2026-05-31,
/// all expecting recommendedDate 2026-12-01 for Dose 3 (6-month recommended interval from a
/// dose-2 date, age-dominated to DOB + 6 months - which the old, clamping arithmetic put at
/// 2026-11-30 instead).
/// </summary>
public class DateRolloverInvestigationTests : IClassFixture<ReferenceDataFixture>
{
    private readonly ReferenceDataFixture _fixture;

    public DateRolloverInvestigationTests(ReferenceDataFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Real_2013_0003_DTaPDose2At10Weeks_RecommendedDateNowCorrect()
    {
        // Real verification, not a diagnostic - confirms the DurationExpression fix directly
        // against this exact real corpus case before trusting it against the full 1,064-case
        // corpus. The corpus's own expected forecast for this case: forecastNumber 3, earliestDate
        // 2026-09-06 (already correct, unaffected by this fix), recommendedDate 2026-12-01,
        // pastDueDate 2027-01-27.
        var repo = _fixture.Repository;

        var patient = new Patient { PatientId = "diag-2013-0003-verify", DateOfBirth = new DateOnly(2026, 5, 31) };
        var assessmentDate = new DateOnly(2026, 8, 5);
        var doses = new[]
        {
            new VaccineDoseAdministered { DoseId = "d1", Cvx = "107", DateAdministered = new DateOnly(2026, 7, 12) },
            new VaccineDoseAdministered { DoseId = "d2", Cvx = "107", DateAdministered = new DateOnly(2026, 8, 5) }
        };

        var fullResult = GeneratePatientForecast.ExecuteWithDoseDetail(
            patient, doses, repo.AllSeries, repo.Schedule, repo.VaccineGroups,
            repo.ImmunityByAntigen, repo.ContraindicationsByAntigen, assessmentDate);
        var dtapGroup = fullResult.VaccineGroupForecasts.SingleOrDefault(g => g.VaccineGroupName.Trim() == "DTaP/Tdap/Td");

        Assert.NotNull(dtapGroup);
        Assert.Equal(new DateOnly(2026, 9, 6), dtapGroup!.EarliestDate);
        Assert.Equal(new DateOnly(2026, 12, 1), dtapGroup.AdjustedRecommendedDate);
    }
}
