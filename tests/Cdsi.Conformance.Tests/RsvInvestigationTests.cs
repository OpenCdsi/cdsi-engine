/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.Evaluation;
using Cdsi.Core.Models;
using Cdsi.Core.Pipeline;
using Xunit;

namespace Cdsi.Conformance.Tests;

/// <summary>
/// Investigating (and, for 2024-0056, confirming a real fix for) a consistent pattern across 5
/// of the corpus's 8 real RSV failures (ages 49-80, different vaccines - Arexvy/CVX303,
/// mResvia/CVX326, Abrysvo): all showed the identical mismatch, seriesStatus expected Complete
/// got AgedOut, and the dose itself expected Valid got NotValid.
///
/// Real RSV data has only 5 series total (much simpler than Pneumococcal's 23) - but two of them
/// are both SeriesType.Standard, both marked defaultSeries=Yes, in DIFFERENT series groups:
/// "RSV 1-dose series" (group 1, no age restriction at all) and "RSV 75 years+ 1-dose series"
/// (group 3, real minAgeToStart is 50 years despite the name, not 75). A 60-80 year old patient
/// is relevant to BOTH groups simultaneously, and both win §8.8 - the same multi-winner shape as
/// the Pneumococcal bug found and fixed earlier this session, except here BOTH winners are
/// SeriesType.Standard, so that fix's Standard-over-Risk preference alone didn't disambiguate
/// between them. Confirmed via a real diagnostic (DiagnosticOnly_..., kept below): "RSV 1-dose
/// series" comes back AgedOut (dose flagged "Inadvertent Administration" - genuinely not meant
/// for this patient), "RSV 75 years+ 1-dose series" correctly comes back Complete.
///
/// FIRST FIX ATTEMPT, CONFIRMED INCOMPLETE BY REAL EXECUTION: extended GeneratePatientForecast's
/// existing representative-series selection (see its own doc comment) to prefer whichever winner
/// is NOT AgedOut. This is genuinely correct and kept - it fixes the per-dose Valid/NotValid
/// conformance detail (doseDetailsByAntigen), one of the two real mismatches this corpus case
/// has - but a real run against this exact case showed seriesStatus itself was UNCHANGED, still
/// AgedOut. The reason: seriesStatus is computed independently, by MergeVaccineGroupForecast from
/// bestSeriesByVaccineGroup - a completely different list that fix never touched, still
/// containing both winners unchanged.
///
/// SECOND FIX, addressing the actual seriesStatus mismatch: SingleAntigenVaccineGroup.Status
/// (see its own doc comment for the full derivation) extended with the same reasoning it already
/// applied to NotComplete - Complete is likewise a meaningful, resolved signal via a genuinely
/// applicable alternative path, checked before falling back to the worst-case cascade that let
/// AgedOut outrank it. Both fixes needed together for this real case: one for the per-dose
/// detail, one for seriesStatus.
/// </summary>
public class RsvInvestigationTests : IClassFixture<ReferenceDataFixture>
{
    private readonly ReferenceDataFixture _fixture;

    public RsvInvestigationTests(ReferenceDataFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Real_2024_0056_Arexvy75YearOld_RsvNowCorrectlyComplete()
    {
        // Real verification, not a diagnostic - confirms BOTH fixes together directly against
        // this exact real corpus case before trusting them against the full 1,064-case corpus.
        // The corpus's own expected result for this patient: RSV group seriesStatus Complete, no
        // further forecast (all forecast fields null).
        var repo = _fixture.Repository;

        var patient = new Patient { PatientId = "diag-2024-0056-verify", DateOfBirth = new DateOnly(1951, 7, 2) };
        var assessmentDate = new DateOnly(2026, 8, 5);
        var doses = new[]
        {
            new VaccineDoseAdministered { DoseId = "d1", Cvx = "303", DateAdministered = new DateOnly(2026, 8, 5) }
        };

        var fullResult = GeneratePatientForecast.ExecuteWithDoseDetail(
            patient, doses, repo.AllSeries, repo.Schedule, repo.VaccineGroups,
            repo.ImmunityByAntigen, repo.ContraindicationsByAntigen, assessmentDate);
        var rsvGroup = fullResult.VaccineGroupForecasts.SingleOrDefault(g => g.VaccineGroupName.Trim() == "RSV");

        Assert.NotNull(rsvGroup);
        Assert.Equal(PatientSeriesStatus.Complete, rsvGroup!.Status);
    }

    [Fact]
    public void Real_2013_0576_Dose1PCV15At18Months_StillCorrectlyValid_NoRegression()
    {
        // Regression guard for the earlier Pneumococcal fix (see PneumococcalInvestigationTests),
        // which this RSV fix's new AgedOut tie-break was added right alongside - re-verifying it
        // here specifically because both fixes now live in the same representative-series
        // selection code, and the AgedOut-preference ordering must not change which series wins
        // for the ORIGINAL Standard-vs-Risk case (Pneumococcal's own winners weren't AgedOut, so
        // this should be unaffected, but asserting it directly rather than assuming so).
        var repo = _fixture.Repository;

        var patient = new Patient { PatientId = "diag-2013-0576-rsv-regression-check", DateOfBirth = new DateOnly(2025, 2, 5) };
        var assessmentDate = new DateOnly(2026, 8, 5);
        var doses = new[]
        {
            new VaccineDoseAdministered { DoseId = "d1", Cvx = "215", DateAdministered = new DateOnly(2026, 8, 5) }
        };

        var fullResult = GeneratePatientForecast.ExecuteWithDoseDetail(
            patient, doses, repo.AllSeries, repo.Schedule, repo.VaccineGroups,
            repo.ImmunityByAntigen, repo.ContraindicationsByAntigen, assessmentDate);

        var detail = fullResult.DoseDetailsByAntigen.GetValueOrDefault("Pneumococcal");
        var doseResult = detail?.DoseResults.SingleOrDefault(r => r.AdministeredDose.Cvx == "215");

        Assert.NotNull(doseResult);
        Assert.Equal(TargetDoseStatus.Satisfied, doseResult!.Result.TargetDoseStatus);
        Assert.Equal(EvaluationStatus.Valid, doseResult.Result.EvaluationStatus);
    }

    [Fact]
    public void DiagnosticOnly_2024_0056_Arexvy75YearOld_WhichRsvSeriesWinAndWhy()
    {
        var repo = _fixture.Repository;

        var patient = new Patient { PatientId = "diag-2024-0056", DateOfBirth = new DateOnly(1951, 7, 2) };
        var assessmentDate = new DateOnly(2026, 8, 5);
        var doses = new[]
        {
            new VaccineDoseAdministered { DoseId = "d1", Cvx = "303", DateAdministered = new DateOnly(2026, 8, 5) }
        };

        var relevantSeries = CreateRelevantPatientSeries.Execute(patient, repo.AllSeries, assessmentDate).RelevantSeries
            .Where(s => s.Antigen == "RSV").ToArray();

        var antigenRecords = OrganizeImmunizationHistory.Execute(patient, doses, repo.Schedule.CvxToAntigen);
        var records = antigenRecords.Where(r => r.Antigen == "RSV").OrderBy(r => r.DateAdministered).ToArray();
        bool NoCompletedSeries(string? _) => false;

        var members = new List<SeriesGroupMember>();
        var perSeriesDump = new System.Text.StringBuilder();
        foreach (var series in relevantSeries)
        {
            var history = EvaluateSeriesHistory.Execute(
                patient, series, records, Array.Empty<EvaluatedAntigenDose>(),
                repo.Schedule.ConflictsByImpactedCvx, NoCompletedSeries, assessmentDate);
            var forecast = GeneratePatientSeriesForecast.Execute(
                patient, series, history, assessmentDate,
                repo.ImmunityByAntigen["RSV"], repo.ContraindicationsByAntigen["RSV"],
                Array.Empty<PriorVaccineDoseAdministered>(), repo.Schedule.ConflictsByImpactedCvx, NoCompletedSeries);
            members.Add(new SeriesGroupMember(series, history, forecast));

            var doseResultReasons = string.Join(",", history.DoseResults.Select(r => $"{r.Result.TargetDoseStatus}/{r.Result.EvaluationStatus}/{r.Result.Reason}"));
            perSeriesDump.Append($"[{series.SeriesName}: SeriesType={series.SeriesType} Status={forecast.Status} CurrentTargetDoseNumber={history.CurrentTargetDoseNumber} DoseResults=({doseResultReasons})] ");
        }

        var best = DetermineBestPatientSeriesForAntigen.Execute(members, patient.DateOfBirth, assessmentDate);
        var bestNames = string.Join(", ", best.Select(s => s.SeriesName));

        Assert.True(false, $"Relevant series count={relevantSeries.Length} || {perSeriesDump} || BEST: {bestNames}");
    }
}
