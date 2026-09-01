/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Core.Evaluation;
using OpenCdsi.VaxEngine.Core.Models;
using OpenCdsi.VaxEngine.Core.Pipeline;
using Xunit;

namespace OpenCdsi.VaxEngine.Conformance.Tests;

/// <summary>
/// DIAGNOSTIC ONLY - investigating a distinct HepB/HepA failure pattern (part of the corpus's 38
/// real HepB/HepA failures), separate from the DTaP-family re-forecast-loop cascade already
/// investigated and left unresolved (see GeneratePatientSeriesForecast's own class doc comment)
/// and from the Pneumococcal/MenB bugs already found and fixed this session.
///
/// Real corpus case 2026-0006 (DOB 1999-08-05, one Twinrix/CVX104 dose at 27 years old): corpus
/// expects seriesStatus NotComplete and the dose itself Valid - engine says seriesStatus AgedOut
/// and the dose Extraneous. The FIRST diagnostic (DiagnosticOnly_HepB_...) traced HepB's own
/// series selection for this patient and found it already correct: "HepB Twinrix 3 Dose Series"
/// wins outright, NotComplete, Dose 1 Valid, correctly advanced to Dose 2 - so HepB isn't the bug
/// at all. The corpus's own raw data resolved which group actually matters: `"vaccineGroup":
/// "HepA"`, not HepB - Twinrix is a combination vaccine, and this test case is specifically
/// checking the HepA side of it.
///
/// For HepA specifically, real data shows something structurally different from HepB: there is
/// NO Standard-type Twinrix series at all - every Twinrix-containing HepA series is Risk (or
/// Evaluation Only), and the one real Standard series ("HepA 2-dose series") has maxAgeToStart
/// 19 years, which this 27-year-old is well past. So only a Risk-type series can plausibly apply
/// here - via SELECTSCORE-2's bullet 1 (a Risk series with priority as good as or better than
/// every series in its group can become scorable on its own, independent of the Standard-series
/// bullets). Tracing HepA's own series selection directly (DiagnosticOnly_HepA_...) to see
/// whether that's actually happening.
/// </summary>
public class HepATwinrixInvestigationTests : IClassFixture<ReferenceDataFixture>
{
    private readonly ReferenceDataFixture _fixture;

    public HepATwinrixInvestigationTests(ReferenceDataFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void DiagnosticOnly_HepB_2026_0006_Twinrix27YearOld_WhichSeriesWinsAndWhy()
    {
        var repo = _fixture.Repository;

        var patient = new Patient { PatientId = "diag-2026-0006-hepb", DateOfBirth = new DateOnly(1999, 8, 5) };
        var assessmentDate = new DateOnly(2026, 8, 5);
        var doses = new[]
        {
            new VaccineDoseAdministered { DoseId = "d1", Cvx = "104", DateAdministered = new DateOnly(2026, 8, 5) }
        };

        var relevantSeries = CreateRelevantPatientSeries.Execute(patient, repo.AllSeries, assessmentDate).RelevantSeries
            .Where(s => s.Antigen == "HepB").ToArray();

        var antigenRecords = OrganizeImmunizationHistory.Execute(patient, doses, repo.Schedule.CvxToAntigen);
        var records = antigenRecords.Where(r => r.Antigen == "HepB").OrderBy(r => r.DateAdministered).ToArray();
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
                repo.ImmunityByAntigen["HepB"], repo.ContraindicationsByAntigen["HepB"],
                Array.Empty<PriorVaccineDoseAdministered>(), repo.Schedule.ConflictsByImpactedCvx, NoCompletedSeries);
            members.Add(new SeriesGroupMember(series, history, forecast));

            var doseResultReasons = string.Join(",", history.DoseResults.Select(r => $"{r.Result.TargetDoseStatus}/{r.Result.EvaluationStatus}/{r.Result.Reason}"));
            perSeriesDump.Append($"[{series.SeriesName}: Status={forecast.Status} CurrentTargetDoseNumber={history.CurrentTargetDoseNumber} DoseResults=({doseResultReasons})] ");
        }

        var best = DetermineBestPatientSeriesForAntigen.Execute(members, patient.DateOfBirth, assessmentDate);
        var bestNames = string.Join(", ", best.Select(s => s.SeriesName));

        Assert.True(false, $"Relevant series count={relevantSeries.Length} || {perSeriesDump} || BEST: {bestNames}");
    }

    [Fact]
    public void DiagnosticOnly_HepA_2026_0006_Twinrix27YearOld_WhichSeriesWinsAndWhy()
    {
        // The real vaccineGroup this corpus case actually checks (confirmed from the corpus's
        // own raw JSON: "vaccineGroup": "HepA") - see this class's own doc comment for why HepA,
        // not HepB, structurally lacks a Standard-type Twinrix series entirely, and why only a
        // Risk-type series (via SELECTSCORE-2's bullet 1) can plausibly apply to this patient.
        var repo = _fixture.Repository;

        var patient = new Patient { PatientId = "diag-2026-0006-hepa", DateOfBirth = new DateOnly(1999, 8, 5) };
        var assessmentDate = new DateOnly(2026, 8, 5);
        var doses = new[]
        {
            new VaccineDoseAdministered { DoseId = "d1", Cvx = "104", DateAdministered = new DateOnly(2026, 8, 5) }
        };

        var relevantSeries = CreateRelevantPatientSeries.Execute(patient, repo.AllSeries, assessmentDate).RelevantSeries
            .Where(s => s.Antigen == "HepA").ToArray();

        var antigenRecords = OrganizeImmunizationHistory.Execute(patient, doses, repo.Schedule.CvxToAntigen);
        var records = antigenRecords.Where(r => r.Antigen == "HepA").OrderBy(r => r.DateAdministered).ToArray();
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
                repo.ImmunityByAntigen["HepA"], repo.ContraindicationsByAntigen["HepA"],
                Array.Empty<PriorVaccineDoseAdministered>(), repo.Schedule.ConflictsByImpactedCvx, NoCompletedSeries);
            members.Add(new SeriesGroupMember(series, history, forecast));

            var doseResultReasons = string.Join(",", history.DoseResults.Select(r => $"{r.Result.TargetDoseStatus}/{r.Result.EvaluationStatus}/{r.Result.Reason}"));
            perSeriesDump.Append($"[{series.SeriesName}: Status={forecast.Status} CurrentTargetDoseNumber={history.CurrentTargetDoseNumber} DoseResults=({doseResultReasons})] ");
        }

        var best = DetermineBestPatientSeriesForAntigen.Execute(members, patient.DateOfBirth, assessmentDate);
        var bestNames = string.Join(", ", best.Select(s => s.SeriesName));

        Assert.True(false, $"Relevant series count={relevantSeries.Length} || {perSeriesDump} || BEST: {bestNames}");
    }
}
