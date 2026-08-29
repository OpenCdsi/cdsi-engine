/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.Evaluation;
using Cdsi.Core.Models;
using Cdsi.Core.Pipeline;
using Xunit;

namespace Cdsi.Conformance.Tests;

/// <summary>
/// Investigating (and, for 2013-0576, confirming a real fix for) the Pneumococcal failure family
/// (60 of the corpus's 255 pre-fix failures, the largest unexplored category at the time). Real
/// corpus case 2013-0576: dose #1 PCV15 given at 18 months (DOB 2025-02-05, dose 2026-08-05) -
/// corpus expects Valid, engine said NotValid/TargetDoseStatus=NotSatisfied/"Too young".
///
/// Checked first whether §8's own series selection is wrongly picking a series whose Dose 1 age
/// window excludes this patient - but the real SELECTSCORE-2 spec text, read directly, confirms
/// the engine's own PreFilterPatientSeries implementation matches it exactly. So series
/// SELECTION isn't the bug - but a real diagnostic replicating GeneratePatientForecast's own
/// pipeline flow directly (DiagnosticOnly_..._WhichSeriesAreRelevantAndWhichWinsAsBest, kept
/// below) found the real cause one step later: DetermineBestPatientSeriesForAntigen correctly
/// returns MULTIPLE winning series for this antigen (a real, documented §8.8 outcome) - the
/// correct "Pneumococcal start at 12 months series" AND the irrelevant "Pneumococcal 50+ 1-dose
/// PCV series" (a Risk series for a completely different age bracket) - and
/// GeneratePatientForecast's own doseDetailsByAntigen assignment picked whichever happened to
/// come last in an unspecified iteration order, silently overwriting the correct result. Fixed
/// in GeneratePatientForecast.cs itself (see its own doc comment for the full derivation) by
/// explicitly preferring SeriesType.Standard when multiple winners exist, rather than relying on
/// implicit last-write-wins ordering.
/// </summary>
public class PneumococcalInvestigationTests : IClassFixture<ReferenceDataFixture>
{
    private readonly ReferenceDataFixture _fixture;

    public PneumococcalInvestigationTests(ReferenceDataFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Real_2013_0576_Dose1PCV15At18Months_NowCorrectlyValid()
    {
        // Real verification, not a diagnostic - confirms the GeneratePatientForecast fix directly
        // against this exact real corpus case before trusting it against the full 1,064-case
        // corpus. The corpus's own single mismatch for this case (confirmed earlier, before this
        // fix) was specifically the dose's own Valid/NotValid status - everything else about the
        // forecast was already correct, so this test only needs to check that.
        var repo = _fixture.Repository;

        var patient = new Patient { PatientId = "diag-2013-0576-verify", DateOfBirth = new DateOnly(2025, 2, 5) };
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
    public void DiagnosticOnly_2013_0576_Dose1PCV15At18Months_WhichSeriesAndWhyNotSatisfied()
    {
        var repo = _fixture.Repository;

        var patient = new Patient { PatientId = "diag-2013-0576", DateOfBirth = new DateOnly(2025, 2, 5) };
        var assessmentDate = new DateOnly(2026, 8, 5);
        var doses = new[]
        {
            new VaccineDoseAdministered { DoseId = "d1", Cvx = "215", DateAdministered = new DateOnly(2026, 8, 5) }
        };

        var fullResult = GeneratePatientForecast.ExecuteWithDoseDetail(
            patient, doses, repo.AllSeries, repo.Schedule, repo.VaccineGroups,
            repo.ImmunityByAntigen, repo.ContraindicationsByAntigen, assessmentDate);

        var detail = fullResult.DoseDetailsByAntigen.GetValueOrDefault("Pneumococcal");
        var doseResultsDump = detail is null
            ? "NO DETAIL FOR Pneumococcal AT ALL"
            : string.Join(" | ", detail.DoseResults.Select(r =>
                $"TargetDose={r.TargetDoseNumber} Cvx={r.AdministeredDose.Cvx} TargetDoseStatus={r.Result.TargetDoseStatus} EvaluationStatus={r.Result.EvaluationStatus} Reason={r.Result.Reason}"));
        var evaluatedDump = detail is null
            ? ""
            : string.Join(" | ", detail.AllEvaluatedDoses.Select(d =>
                $"Cvx={d.Cvx} Status={d.Status} SatisfiedTargetDoseNumber={d.SatisfiedTargetDoseNumber}"));

        Assert.True(false, $"CurrentTargetDoseNumber={detail?.CurrentTargetDoseNumber} || DoseResults: {doseResultsDump} || AllEvaluatedDoses: {evaluatedDump}");
    }

    [Fact]
    public void DiagnosticOnly_2013_0576_Dose1PCV15At18Months_WhichSeriesAreRelevantAndWhichWinsAsBest()
    {
        // DIAGNOSTIC, not a fix. The first diagnostic above showed the WINNING series' own Dose 1
        // evaluation says "Too young" for an 18-month-old - genuinely surprising against the
        // 4-dose series's own Dose 1 (minAge 6 weeks, confirmed via the real XML). Replicating
        // GeneratePatientForecast's own real flow (CreateRelevantPatientSeries ->
        // EvaluateSeriesHistory + GeneratePatientSeriesForecast per relevant series ->
        // DetermineBestPatientSeriesForAntigen) directly, to see EVERY relevant Pneumococcal
        // series's own CurrentTargetDoseNumber/reason side by side, and which one(s)
        // DetermineBestPatientSeriesForAntigen actually selects as "best" - rather than assuming
        // the 4-dose series is what's actually being evaluated.
        var repo = _fixture.Repository;

        var patient = new Patient { PatientId = "diag-2013-0576b", DateOfBirth = new DateOnly(2025, 2, 5) };
        var assessmentDate = new DateOnly(2026, 8, 5);
        var doses = new[]
        {
            new VaccineDoseAdministered { DoseId = "d1", Cvx = "215", DateAdministered = new DateOnly(2026, 8, 5) }
        };

        var relevantSeries = CreateRelevantPatientSeries.Execute(patient, repo.AllSeries, assessmentDate).RelevantSeries
            .Where(s => s.Antigen == "Pneumococcal").ToArray();

        var antigenRecords = OrganizeImmunizationHistory.Execute(patient, doses, repo.Schedule.CvxToAntigen);
        var records = antigenRecords.Where(r => r.Antigen == "Pneumococcal").OrderBy(r => r.DateAdministered).ToArray();
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
                repo.ImmunityByAntigen["Pneumococcal"], repo.ContraindicationsByAntigen["Pneumococcal"],
                Array.Empty<PriorVaccineDoseAdministered>(), repo.Schedule.ConflictsByImpactedCvx, NoCompletedSeries);
            members.Add(new SeriesGroupMember(series, history, forecast));

            var doseResultReasons = string.Join(",", history.DoseResults.Select(r => $"{r.Result.TargetDoseStatus}/{r.Result.Reason}"));
            perSeriesDump.Append($"[{series.SeriesName}: CurrentTargetDoseNumber={history.CurrentTargetDoseNumber} DoseResults=({doseResultReasons})] ");
        }

        var best = DetermineBestPatientSeriesForAntigen.Execute(members, patient.DateOfBirth, assessmentDate);
        var bestNames = string.Join(", ", best.Select(s => s.SeriesName));

        Assert.True(false, $"Relevant series count={relevantSeries.Length} || {perSeriesDump} || BEST: {bestNames}");
    }
}
