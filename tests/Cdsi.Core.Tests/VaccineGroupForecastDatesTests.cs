using Cdsi.Core.Evaluation;
using Cdsi.Core.ReferenceData;
using Xunit;

namespace Cdsi.Core.Tests;

public class VaccineGroupForecastDatesTests
{
    [Fact]
    public void AdjustedRecommendedDate_TakesLaterOfEarliestContainedAndGroupEarliestDate()
    {
        var groupEarliest = new DateOnly(2024, 6, 1);
        var contained = new[] { new DateOnly(2024, 3, 1), new DateOnly(2024, 8, 1) }; // earliest = 2024-03-01

        var result = VaccineGroupForecastDates.AdjustedRecommendedDate(groupEarliest, contained);

        // 2024-03-01 (earliest contained) < groupEarliest (2024-06-01), so the group's own
        // earliest date wins per FORECASTVG-2's "latest of the two" rule.
        Assert.Equal(groupEarliest, result);
    }

    [Fact]
    public void AdjustedRecommendedDate_ContainedDateCanWin_WhenLaterThanGroupEarliestDate()
    {
        var groupEarliest = new DateOnly(2024, 1, 1);
        var contained = new[] { new DateOnly(2024, 6, 1), new DateOnly(2024, 8, 1) };

        var result = VaccineGroupForecastDates.AdjustedRecommendedDate(groupEarliest, contained);

        Assert.Equal(new DateOnly(2024, 6, 1), result);
    }

    [Fact]
    public void AdjustedPastDueDate_NullWhenNoContainedForecastHasOne()
    {
        var result = VaccineGroupForecastDates.AdjustedPastDueDate(new DateOnly(2024, 1, 1), new DateOnly?[] { null, null });
        Assert.Null(result);
    }

    [Fact]
    public void AdjustedPastDueDate_IgnoresNullsAmongMixedContainedValues()
    {
        var groupEarliest = new DateOnly(2024, 1, 1);
        var contained = new DateOnly?[] { null, new DateOnly(2024, 5, 1), new DateOnly(2024, 9, 1) };

        var result = VaccineGroupForecastDates.AdjustedPastDueDate(groupEarliest, contained);

        Assert.Equal(new DateOnly(2024, 5, 1), result);
    }

    [Fact]
    public void LatestDate_EarliestOfContainedLatestDates_NullIfNoneHaveOne()
    {
        Assert.Equal(new DateOnly(2024, 3, 1), VaccineGroupForecastDates.LatestDate(new DateOnly?[] { new DateOnly(2024, 3, 1), new DateOnly(2025, 1, 1) }));
        Assert.Null(VaccineGroupForecastDates.LatestDate(new DateOnly?[] { null, null }));
    }

    [Fact]
    public void UnadjustedRecommendedDate_EarliestAmongContained()
    {
        var result = VaccineGroupForecastDates.UnadjustedRecommendedDate(new[] { new DateOnly(2024, 5, 1), new DateOnly(2024, 2, 1) });
        Assert.Equal(new DateOnly(2024, 2, 1), result);
    }

    [Fact]
    public void UnadjustedPastDueDate_EarliestAmongContained_NullIfNoneHaveOne()
    {
        Assert.Equal(new DateOnly(2024, 2, 1), VaccineGroupForecastDates.UnadjustedPastDueDate(new DateOnly?[] { new DateOnly(2024, 5, 1), new DateOnly(2024, 2, 1) }));
        Assert.Null(VaccineGroupForecastDates.UnadjustedPastDueDate(new DateOnly?[] { null }));
    }

    [Fact]
    public void ForecastDoseNumber_RealMmrFlag_UsesMinimum()
    {
        // Real data: MMR's administerFullVaccineGroup is "Yes".
        var mmrFlag = ScheduleSupportingDataLoader.LoadVaccineGroups(TestPaths.ScheduleFilePath)
            .Single(g => g.Name == "MMR").AdministerFullVaccineGroup;

        Assert.True(mmrFlag);

        var result = VaccineGroupForecastDates.ForecastDoseNumber(mmrFlag!.Value, new[] { 2, 3, 1 });

        Assert.Equal(1, result);
    }

    [Fact]
    public void ForecastDoseNumber_RealDTaPFlag_UsesMaximum()
    {
        // Real data: DTaP/Tdap/Td's administerFullVaccineGroup is "No".
        var dtapFlag = ScheduleSupportingDataLoader.LoadVaccineGroups(TestPaths.ScheduleFilePath)
            .Single(g => g.Name == "DTaP/Tdap/Td").AdministerFullVaccineGroup;

        Assert.False(dtapFlag);

        var result = VaccineGroupForecastDates.ForecastDoseNumber(dtapFlag!.Value, new[] { 2, 3, 1 });

        Assert.Equal(3, result);
    }

    [Fact]
    public void SingleAntigenVaccineGroup_Status_PassesThroughTheSingleContainedStatus()
    {
        var result = SingleAntigenVaccineGroup.Status(new[] { PatientSeriesStatus.NotComplete });
        Assert.Equal(PatientSeriesStatus.NotComplete, result);
    }

    [Fact]
    public void SingleAntigenVaccineGroup_Status_MultipleIdenticalContainedStatuses_ResolvesCleanly()
    {
        // A single antigen can legitimately have more than one "best patient series" (§8.8) -
        // e.g. two equivalent series groups both independently landing on Complete. Redundant
        // agreement, not an inconsistency - should resolve without throwing.
        var result = SingleAntigenVaccineGroup.Status(new[] { PatientSeriesStatus.Complete, PatientSeriesStatus.Complete });
        Assert.Equal(PatientSeriesStatus.Complete, result);
    }

    [Fact]
    public void SingleAntigenVaccineGroup_Status_AnyContainedNotComplete_AlwaysWins_RealCrashScenario()
    {
        // The exact real scenario that crashed running the full catalog: a newborn's antigen
        // produced two best series, one NotRecommended (that specific path has nothing due
        // right now) and one NotComplete (a dose genuinely is due via a different path). The
        // actionable status must win - reporting NotRecommended here would hide a real
        // recommendation behind an alternative, non-chosen path's non-recommendation.
        var result = SingleAntigenVaccineGroup.Status(new[] { PatientSeriesStatus.NotRecommended, PatientSeriesStatus.NotComplete });
        Assert.Equal(PatientSeriesStatus.NotComplete, result);
    }

    [Fact]
    public void SingleAntigenVaccineGroup_Status_NoContainedNotComplete_FallsBackToWorstCaseCascade()
    {
        // Neither path is actionable (NotComplete) - falls back to MultipleAntigenVaccineGroup's
        // own worst-case cascade, which ranks Contraindicated ahead of Immune.
        var result = SingleAntigenVaccineGroup.Status(new[] { PatientSeriesStatus.Contraindicated, PatientSeriesStatus.Immune });
        Assert.Equal(PatientSeriesStatus.Contraindicated, result);
    }

    [Fact]
    public void SingleAntigenVaccineGroup_EarliestDate_TakesTheMinimum()
    {
        var result = SingleAntigenVaccineGroup.EarliestDate(new[] { new DateOnly(2024, 5, 1), new DateOnly(2024, 2, 1) });
        Assert.Equal(new DateOnly(2024, 2, 1), result);
    }
}
