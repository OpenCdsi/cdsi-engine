using Cdsi.Core.Evaluation;
using Xunit;

namespace Cdsi.Core.Tests;

public class ClassifyScorablePatientSeriesTests
{
    [Fact]
    public void IsCompletePatientSeries_TrueOnlyForCompleteStatus()
    {
        Assert.True(ClassifyScorablePatientSeries.IsCompletePatientSeries(PatientSeriesStatus.Complete));
        Assert.False(ClassifyScorablePatientSeries.IsCompletePatientSeries(PatientSeriesStatus.NotComplete));
        Assert.False(ClassifyScorablePatientSeries.IsCompletePatientSeries(PatientSeriesStatus.Immune));
    }

    [Fact]
    public void IsInProcessPatientSeries_RequiresBothSatisfiedDoseAndNotCompleteStatus()
    {
        Assert.True(ClassifyScorablePatientSeries.IsInProcessPatientSeries(true, PatientSeriesStatus.NotComplete));
        Assert.False(ClassifyScorablePatientSeries.IsInProcessPatientSeries(false, PatientSeriesStatus.NotComplete));
        Assert.False(ClassifyScorablePatientSeries.IsInProcessPatientSeries(true, PatientSeriesStatus.Complete));
    }

    [Fact]
    public void CountValidDoses_CountsOnlySatisfiedStatuses()
    {
        var statuses = new[]
        {
            TargetDoseStatus.Satisfied,
            TargetDoseStatus.NotSatisfied,
            TargetDoseStatus.Satisfied,
            TargetDoseStatus.Skipped
        };

        var result = ClassifyScorablePatientSeries.CountValidDoses(statuses);

        Assert.Equal(2, result);
    }

    [Fact]
    public void ScoringCategory_TwoOrMoreComplete_ScoresComplete()
    {
        var result = ClassifyScorablePatientSeries.DetermineScoringCategory(completeCount: 2, inProcessCount: 0, allScorableSeriesHaveZeroValidDoses: false);
        Assert.Equal(ScoringCategory.CompletePatientSeries, result);
    }

    [Fact]
    public void ScoringCategory_TwoOrMoreComplete_TakesPriorityOverInProcess()
    {
        // If both conditions could apply, Table 8-5's column 1 (complete>=2) is checked first.
        var result = ClassifyScorablePatientSeries.DetermineScoringCategory(completeCount: 3, inProcessCount: 5, allScorableSeriesHaveZeroValidDoses: false);
        Assert.Equal(ScoringCategory.CompletePatientSeries, result);
    }

    [Fact]
    public void ScoringCategory_NoComplete_TwoOrMoreInProcess_ScoresInProcess()
    {
        var result = ClassifyScorablePatientSeries.DetermineScoringCategory(completeCount: 0, inProcessCount: 2, allScorableSeriesHaveZeroValidDoses: false);
        Assert.Equal(ScoringCategory.InProcessPatientSeries, result);
    }

    [Fact]
    public void ScoringCategory_NoCompleteNoInProcess_AllZeroValidDoses_ScoresNoValidDoses()
    {
        var result = ClassifyScorablePatientSeries.DetermineScoringCategory(completeCount: 0, inProcessCount: 0, allScorableSeriesHaveZeroValidDoses: true);
        Assert.Equal(ScoringCategory.NoValidDoses, result);
    }

    [Fact]
    public void ScoringCategory_NoRuleMatches_Undetermined()
    {
        // Not spec-covered combination: no complete, no in-process, but some series has valid doses.
        var result = ClassifyScorablePatientSeries.DetermineScoringCategory(completeCount: 0, inProcessCount: 0, allScorableSeriesHaveZeroValidDoses: false);
        Assert.Equal(ScoringCategory.Undetermined, result);
    }
}
