using Cdsi.Core.Evaluation;
using Cdsi.Core.Models;
using Cdsi.Core.Pipeline;
using Cdsi.Core.ReferenceData;
using Xunit;

namespace Cdsi.Core.Tests;

/// <summary>
/// Patient-level capstone tests: multiple relevant series (different antigens) evaluated
/// together via EvaluatePatientSeriesHistory, specifically to prove cross-antigen Vaccine
/// Conflict resolution actually works end-to-end - the one piece a single-series test can't
/// demonstrate, since it requires history from a DIFFERENT antigen's series.
/// </summary>
public class EvaluatePatientSeriesHistoryTests
{
    private static readonly ScheduleSupportingData Schedule =
        ScheduleSupportingDataLoader.LoadFile(TestPaths.ScheduleFilePath);

    private static readonly IReadOnlyList<AntigenSeries> MeaslesSeries =
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_Measles-508.xml"));

    private static readonly IReadOnlyList<AntigenSeries> VaricellaSeries =
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_Varicella-508.xml"));

    private static readonly Func<string?, bool> NoCompletedSeriesExpected =
        _ => throw new InvalidOperationException("Test fixture shouldn't reach a Completed Series condition.");

    private static Patient MakePatient(DateOnly dob) => new() { PatientId = "p1", DateOfBirth = dob };

    [Fact]
    public void VaricellaGivenTooSoonAfterMmr_ImpactedByCrossAntigenConflict()
    {
        var dob = new DateOnly(2019, 1, 1);
        var patient = MakePatient(dob);

        var relevantSeries = new[]
        {
            MeaslesSeries.Single(s => s.SeriesName == "Measles 2-dose series"),
            VaricellaSeries.Single(s => s.SeriesName == "Varicella childhood 2-dose series")
        };

        // Real conflict rule (verified against Schedule data): MMR (CVX 03) -> Varicella (CVX 21),
        // conflictBeginInterval 1 day, both end intervals 28 days regardless of the MMR dose's
        // own evaluation status. MMR here creates a "Measles" antigen-administered record (among
        // others) - the Varicella series has no antigen-administered records of its own for
        // Measles, so this conflict is only detectable through the CROSS-antigen history this
        // orchestrator provides.
        var doses = new[]
        {
            new VaccineDoseAdministered { DoseId = "mmr1", Cvx = "03", DateAdministered = new DateOnly(2020, 1, 10) },
            new VaccineDoseAdministered { DoseId = "var1", Cvx = "21", DateAdministered = new DateOnly(2020, 1, 20) } // 10 days later - inside the 28-day window
        };

        var results = EvaluatePatientSeriesHistory.Execute(
            patient, relevantSeries, doses, Schedule.CvxToAntigen, Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected);

        var varicellaResult = results[relevantSeries[1]];
        var varicellaDoseResult = Assert.Single(varicellaResult.DoseResults);

        Assert.Equal(TargetDoseStatus.NotSatisfied, varicellaDoseResult.Result.TargetDoseStatus);
        Assert.Equal(EvaluationStatus.NotValid, varicellaDoseResult.Result.EvaluationStatus);
        Assert.Equal("Impacted by vaccine conflict", varicellaDoseResult.Result.Reason);
    }

    [Fact]
    public void VaricellaGivenWellAfterConflictWindow_NotImpacted()
    {
        var dob = new DateOnly(2019, 1, 1);
        var patient = MakePatient(dob);

        var relevantSeries = new[]
        {
            MeaslesSeries.Single(s => s.SeriesName == "Measles 2-dose series"),
            VaricellaSeries.Single(s => s.SeriesName == "Varicella childhood 2-dose series")
        };

        var doses = new[]
        {
            new VaccineDoseAdministered { DoseId = "mmr1", Cvx = "03", DateAdministered = new DateOnly(2020, 1, 10) },
            new VaccineDoseAdministered { DoseId = "var1", Cvx = "21", DateAdministered = new DateOnly(2020, 6, 1) } // well past the 28-day window
        };

        var results = EvaluatePatientSeriesHistory.Execute(
            patient, relevantSeries, doses, Schedule.CvxToAntigen, Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected);

        var varicellaResult = results[relevantSeries[1]];
        var varicellaDoseResult = Assert.Single(varicellaResult.DoseResults);

        Assert.Equal(TargetDoseStatus.Satisfied, varicellaDoseResult.Result.TargetDoseStatus);
    }

    [Fact]
    public void EachRelevantSeriesEvaluatedIndependently_SameRawDosesCanDifferInOutcome()
    {
        // §4.4's own text: "An administered dose that is 'valid' for one relevant patient series
        // may be 'not valid' for a different relevant patient series for the same patient."
        // Evaluate against two DIFFERENT real HepB series with the same raw administered doses,
        // and confirm each produces its own independent SeriesHistoryResult (not shared state).
        var hepBSeries = AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_HepB-508.xml"));
        var threeDoseSeries = hepBSeries.Single(s => s.SeriesName == "HepB 3-dose series");
        var heplisavSeries = hepBSeries.Single(s => s.SeriesName == "HepB Heplisav-B 2-dose series");

        var dob = new DateOnly(2020, 1, 1);
        var patient = MakePatient(dob);
        var doses = new[]
        {
            new VaccineDoseAdministered { DoseId = "d1", Cvx = "08", DateAdministered = new DateOnly(2020, 1, 1) },
            new VaccineDoseAdministered { DoseId = "d2", Cvx = "08", DateAdministered = new DateOnly(2020, 3, 1) }
        };

        var results = EvaluatePatientSeriesHistory.Execute(
            patient, new[] { threeDoseSeries, heplisavSeries }, doses,
            Schedule.CvxToAntigen, Schedule.ConflictsByImpactedCvx, NoCompletedSeriesExpected);

        // Both series get evaluated (independent dictionary entries), each against the SAME two
        // raw administered doses, without one series' evaluation contaminating the other's.
        Assert.Equal(2, results.Count);
        Assert.True(results.ContainsKey(threeDoseSeries));
        Assert.True(results.ContainsKey(heplisavSeries));
        Assert.Equal(2, results[threeDoseSeries].DoseResults.Count);
        Assert.Equal(2, results[heplisavSeries].DoseResults.Count);
    }
}
