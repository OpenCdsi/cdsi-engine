/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Core.Models;
using OpenCdsi.VaxEngine.Core.Pipeline;
using Xunit;

namespace OpenCdsi.VaxEngine.Conformance.Tests;

/// <summary>
/// Verifies the real, multi-antigen merge behavior for the DTaP/Tdap/Td group's own §7.6
/// re-forecast-loop bug (see GeneratePatientSeriesForecast's own class doc comment for the full,
/// two-attempt story - both the original unconditional Option 1 and the narrower "exactly one
/// valid dose" version were tried and reverted after real regressions). Uses the SAME real, full
/// reference data every other conformance test uses (ReferenceDataFixture), rather than a
/// hand-built single-antigen series like the OpenCdsi.VaxEngine.Core.Tests diagnostics - needed here
/// specifically because this investigation is about how Pertussis, Diphtheria, and Tetanus's
/// INDIVIDUAL forecasts combine via §9's real merge logic, which a single-antigen test can't show.
/// Kept, even though both fix attempts were reverted, as regression guards on the CURRENT
/// (reverted) baseline for the three real cases this investigation surfaced - and as a live
/// example of exactly the multi-antigen check any future attempt at this bug needs to run before
/// being trusted, not after.
/// </summary>
public class MergeInvestigationTests : IClassFixture<ReferenceDataFixture>
{
    private readonly ReferenceDataFixture _fixture;

    public MergeInvestigationTests(ReferenceDataFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Real_2013_0016_MultiDosePertussisPatient_MergedGroupForecastIsCorrect()
    {
        // 2013-0016: passes on the current, reverted baseline without any fix at all - Diphtheria
        // and Tetanus (which have a real CVX09/Td dose Pertussis's own history doesn't) correctly
        // reach Dose 9 through their own ordinary main-loop mechanics, and the merge's own Min()
        // correctly prefers their 2027-02-05 over Pertussis's own later, Pertussis-specific value.
        var repo = _fixture.Repository;

        var patient = new Patient { PatientId = "diag-2013-0016", DateOfBirth = new DateOnly(2019, 7, 5) };
        var assessmentDate = new DateOnly(2026, 8, 5);
        var doses = new[]
        {
            new VaccineDoseAdministered { DoseId = "d1", Cvx = "107", DateAdministered = new DateOnly(2020, 3, 5) },
            new VaccineDoseAdministered { DoseId = "d2", Cvx = "09", DateAdministered = new DateOnly(2026, 7, 5) },
            new VaccineDoseAdministered { DoseId = "d3", Cvx = "115", DateAdministered = new DateOnly(2026, 8, 5) }
        };

        var fullResult = GeneratePatientForecast.ExecuteWithDoseDetail(
            patient, doses, repo.AllSeries, repo.Schedule, repo.VaccineGroups,
            repo.ImmunityByAntigen, repo.ContraindicationsByAntigen, assessmentDate);
        var groupResult = fullResult.VaccineGroupForecasts.SingleOrDefault(r => r.VaccineGroupName.Trim() == "DTaP/Tdap/Td");

        Assert.Equal(new DateOnly(2027, 2, 5), groupResult?.EarliestDate);
    }

    [Fact]
    public void Real_2013_0067_TdThenTdapPatient_MergedGroupForecastIsCorrect()
    {
        // 2013-0067: found as the SECOND counterexample while the narrowed fix was still in
        // place - CVX09 doesn't map to Pertussis, so this patient also has exactly one valid
        // Pertussis dose, and the narrowed fix's gate fired here too, wrongly, breaking this case
        // the same way it broke 2013-0016.
        //
        // On the CURRENT, reverted baseline, real execution corrected an initial hand-reasoned
        // guess here: this case passes cleanly WITHOUT any fix, matching 2013-0016's own pattern -
        // Diphtheria and Tetanus's own CVX09-driven main-loop mechanics correctly reach Dose 9,
        // and the merge correctly prefers their correct value over whatever Pertussis alone lands
        // on. The original guess in this test (that Pertussis's own cascade would dominate the
        // merge the same way it did for 2020-0004, landing on the same wrong 2026-08-05) was
        // asserted honestly as "reasoned through, not yet confirmed" - and was wrong. Corrected
        // here rather than left in place, the same discipline as every other hand-trace error in
        // this investigation.
        var repo = _fixture.Repository;

        var patient = new Patient { PatientId = "diag-2013-0067", DateOfBirth = new DateOnly(2019, 6, 8) };
        var assessmentDate = new DateOnly(2026, 8, 5);
        var doses = new[]
        {
            new VaccineDoseAdministered { DoseId = "d1", Cvx = "09", DateAdministered = new DateOnly(2026, 7, 8) },
            new VaccineDoseAdministered { DoseId = "d2", Cvx = "115", DateAdministered = new DateOnly(2026, 8, 5) }
        };

        var fullResult = GeneratePatientForecast.ExecuteWithDoseDetail(
            patient, doses, repo.AllSeries, repo.Schedule, repo.VaccineGroups,
            repo.ImmunityByAntigen, repo.ContraindicationsByAntigen, assessmentDate);
        var groupResult = fullResult.VaccineGroupForecasts.SingleOrDefault(r => r.VaccineGroupName.Trim() == "DTaP/Tdap/Td");

        Assert.Equal(new DateOnly(2027, 2, 5), groupResult?.EarliestDate);
    }

    [Fact]
    public void Real_2020_0004_SingleDoseAdultPatient_MergedGroupForecastIsStillWrongOnTheCurrentBaseline()
    {
        // 2020-0004: the original motivating case, still unfixed on the current, reverted
        // baseline. All three antigens have exactly one, identical valid dose, so all three
        // cascade the SAME way to the SAME wrong value - the merge's Min()/priority logic has
        // no correct sibling to prefer, unlike 2013-0016. Asserted here as an honest regression
        // guard on the current baseline's actual (wrong) behavior, not a "this is correct" claim.
        var repo = _fixture.Repository;

        var patient = new Patient { PatientId = "diag-2020-0004", DateOfBirth = new DateOnly(1995, 8, 5) };
        var assessmentDate = new DateOnly(2026, 8, 5);
        var doses = new[]
        {
            new VaccineDoseAdministered { DoseId = "d1", Cvx = "115", DateAdministered = new DateOnly(2026, 8, 5) }
        };

        var fullResult = GeneratePatientForecast.ExecuteWithDoseDetail(
            patient, doses, repo.AllSeries, repo.Schedule, repo.VaccineGroups,
            repo.ImmunityByAntigen, repo.ContraindicationsByAntigen, assessmentDate);
        var groupResult = fullResult.VaccineGroupForecasts.SingleOrDefault(r => r.VaccineGroupName.Trim() == "DTaP/Tdap/Td");

        Assert.Equal(new DateOnly(2026, 8, 5), groupResult?.EarliestDate); // the real corpus expects 2026-09-02 - this is the known-wrong baseline value, not the correct answer
    }
}
