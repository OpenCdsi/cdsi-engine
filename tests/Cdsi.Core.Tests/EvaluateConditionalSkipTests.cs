using Cdsi.Core.Evaluation;
using Cdsi.Core.ReferenceData;
using Xunit;

namespace Cdsi.Core.Tests;

public class EvaluateConditionalSkipTests
{
    private static readonly IReadOnlyList<AntigenSeries> HibSeries =
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_Hib-508.xml"));

    private static readonly IReadOnlyList<AntigenSeries> RabiesSeries =
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_Rabies-508.xml"));

    private static readonly IReadOnlyList<AntigenSeries> HepBSeries =
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_HepB-508.xml"));

    private static readonly Func<string?, bool> NoCompletedSeriesExpected =
        _ => throw new InvalidOperationException("Test fixture shouldn't reach a Completed Series condition.");

    // Real data: "HepB risk Dialysis 4-dose series" Dose 1 has exactly one conditionalSkip
    // instance, context "Both", a single set with a single condition: Completed Series
    // referencing series group "1" (the real "HepB 3-dose series" Standard group). A clean,
    // single-condition fixture - real motivation for building the Completed Series resolver.
    private static IReadOnlyList<ConditionalSkipInstance> HepBDialysisDose1 =>
        HepBSeries.Single(s => s.SeriesName == "HepB risk Dialysis 4-dose series")
            .SeriesDoses.Single(d => d.DoseNumber == 1).ConditionalSkipInstances;

    // Real data: "Hib start at 2 months 4-dose series" Dose 2 has TWO top-level conditionalSkip
    // instances - context Evaluation (beginAge "15 months - 4 days") and context Forecast
    // (beginAge "15 months" exactly, no grace period). Both are loaded; CanBeSkipped's context
    // parameter is what filters which applies.
    private static IReadOnlyList<ConditionalSkipInstance> HibDose2 =>
        HibSeries.Single(s => s.SeriesName == "Hib start at 2 months 4-dose series")
            .SeriesDoses.Single(d => d.DoseNumber == 2).ConditionalSkipInstances;

    // Real data: "Hib start at 2 months 4-dose series" Dose 3 - setLogic OR across two sets:
    // Set 1 (single Age condition, beginAge 12 months) OR
    // Set 2 (Age AND Interval, beginAge 12mo-4d AND interval >= 8wk-4d from previous dose).
    private static IReadOnlyList<ConditionalSkipInstance> HibDose3 =>
        HibSeries.Single(s => s.SeriesName == "Hib start at 2 months 4-dose series")
            .SeriesDoses.Single(d => d.DoseNumber == 3).ConditionalSkipInstances;

    // Real data: "Rabies risk continuous exposure series" Dose 3 - a single Vaccine Count By
    // Date condition: skip if >0 Valid doses of CVX {18,90,175,176} were given on/after
    // 2022-05-06. The SET itself also carries effectiveDate 2022-05-06.
    private static IReadOnlyList<ConditionalSkipInstance> RabiesRiskDose3 =>
        RabiesSeries.Single(s => s.SeriesName == "Rabies risk continuous exposure series")
            .SeriesDoses.Single(d => d.DoseNumber == 3).ConditionalSkipInstances;

    [Fact]
    public void AllInstancesLoadedRegardlessOfContext_BothEvaluationAndForecastPresent()
    {
        // The loader no longer pre-filters by context (§7.1 needs Forecast/Both instances too) -
        // filtering happens in CanBeSkipped via the context parameter instead.
        Assert.Equal(2, HibDose2.Count);
        Assert.Contains(HibDose2, i => i.Context == "Evaluation");
        Assert.Contains(HibDose2, i => i.Context == "Forecast");
    }

    [Fact]
    public void EvaluationContext_UsesTheFourDayGracePeriodThreshold()
    {
        var dob = new DateOnly(2020, 1, 1);
        // Evaluation instance: beginAge "15 months - 4 days" -> 2021-04-01 - 4 days = 2021-03-28.
        var canSkip = EvaluateConditionalSkip.CanBeSkipped(
            dob, new DateOnly(2021, 3, 28), ConditionalSkipContext.Evaluation,
            HibDose2, Array.Empty<PriorVaccineDoseAdministered>(), NoCompletedSeriesExpected);

        Assert.True(canSkip);
    }

    [Fact]
    public void ForecastContext_UsesTheExactFifteenMonthThreshold_NoGracePeriod()
    {
        var dob = new DateOnly(2020, 1, 1);
        // Same reference date (2021-03-28) that satisfies Evaluation's grace-period threshold
        // does NOT satisfy Forecast's exact "15 months" threshold (2021-04-01) - proving the
        // context parameter genuinely selects different real data, not just a label.
        var canSkip = EvaluateConditionalSkip.CanBeSkipped(
            dob, new DateOnly(2021, 3, 28), ConditionalSkipContext.Forecast,
            HibDose2, Array.Empty<PriorVaccineDoseAdministered>(), NoCompletedSeriesExpected);

        Assert.False(canSkip);
    }

    [Fact]
    public void ForecastContext_SatisfiedAtItsOwnExactThreshold()
    {
        var dob = new DateOnly(2020, 1, 1);
        var canSkip = EvaluateConditionalSkip.CanBeSkipped(
            dob, new DateOnly(2021, 4, 1), ConditionalSkipContext.Forecast,
            HibDose2, Array.Empty<PriorVaccineDoseAdministered>(), NoCompletedSeriesExpected);

        Assert.True(canSkip);
    }

    [Fact]
    public void SingleAgeCondition_NotMetBeforeBeginAge()
    {
        var dob = new DateOnly(2020, 1, 1);
        var canSkip = EvaluateConditionalSkip.CanBeSkipped(
            dob, new DateOnly(2021, 1, 1), ConditionalSkipContext.Evaluation,
            HibDose2, Array.Empty<PriorVaccineDoseAdministered>(), NoCompletedSeriesExpected);

        Assert.False(canSkip);
    }

    [Fact]
    public void OrAcrossSets_Set1AloneSatisfied_OverallCanBeSkipped()
    {
        var dob = new DateOnly(2020, 1, 1);
        // Set 1's plain "12 months" age condition is satisfied well past the threshold;
        // Set 2 won't be (no relevant prior doses supplied), but OR only needs one.
        var canSkip = EvaluateConditionalSkip.CanBeSkipped(
            dob, new DateOnly(2021, 6, 1), ConditionalSkipContext.Evaluation,
            HibDose3, Array.Empty<PriorVaccineDoseAdministered>(), NoCompletedSeriesExpected);

        Assert.True(canSkip);
    }

    [Fact]
    public void OrAcrossSets_OnlySet2Satisfied_OverallCanStillBeSkipped()
    {
        var dob = new DateOnly(2020, 1, 1);
        // Reference date 2020-12-30 is BEFORE Set 1's "12 months" threshold (2021-01-01) but
        // AFTER Set 2's "12 months - 4 days" (2020-12-28) - and the interval condition (>= 52
        // days since the previous dose) is satisfied by a dose given 2020-11-01 (59 days prior).
        var priorDoses = new[] { new PriorVaccineDoseAdministered("17", new DateOnly(2020, 11, 1), PriorDoseEvaluationStatus.Valid) };

        var canSkip = EvaluateConditionalSkip.CanBeSkipped(
            dob, new DateOnly(2020, 12, 30), ConditionalSkipContext.Evaluation,
            HibDose3, priorDoses, NoCompletedSeriesExpected);

        Assert.True(canSkip);
    }

    [Fact]
    public void OrAcrossSets_NeitherSetSatisfied_CannotBeSkipped()
    {
        var dob = new DateOnly(2020, 1, 1);
        var priorDoses = new[] { new PriorVaccineDoseAdministered("17", new DateOnly(2020, 5, 1), PriorDoseEvaluationStatus.Valid) };

        // 2020-06-01: too young for either set's age condition, and the interval since the
        // 2020-05-01 prior dose (31 days) is also short of the 52-day requirement.
        var canSkip = EvaluateConditionalSkip.CanBeSkipped(
            dob, new DateOnly(2020, 6, 1), ConditionalSkipContext.Evaluation,
            HibDose3, priorDoses, NoCompletedSeriesExpected);

        Assert.False(canSkip);
    }

    [Fact]
    public void VaccineCountCondition_OneValidPriorDoseOfMatchingType_MeetsGreaterThanZero()
    {
        var dob = new DateOnly(1990, 1, 1);
        var priorDoses = new[] { new PriorVaccineDoseAdministered("18", new DateOnly(2022, 5, 10), PriorDoseEvaluationStatus.Valid) };

        var canSkip = EvaluateConditionalSkip.CanBeSkipped(
            dob, new DateOnly(2022, 6, 1), ConditionalSkipContext.Evaluation,
            RabiesRiskDose3, priorDoses, NoCompletedSeriesExpected);

        Assert.True(canSkip);
    }

    [Fact]
    public void VaccineCountCondition_PriorDoseNotValid_DoesNotCountTowardValidOnlyFilter()
    {
        var dob = new DateOnly(1990, 1, 1);
        var priorDoses = new[] { new PriorVaccineDoseAdministered("18", new DateOnly(2022, 5, 10), PriorDoseEvaluationStatus.NotValid) };

        var canSkip = EvaluateConditionalSkip.CanBeSkipped(
            dob, new DateOnly(2022, 6, 1), ConditionalSkipContext.Evaluation,
            RabiesRiskDose3, priorDoses, NoCompletedSeriesExpected);

        Assert.False(canSkip);
    }

    [Fact]
    public void VaccineCountCondition_PriorDoseBeforeSetsEffectiveDate_SetIsNotApplicable()
    {
        var dob = new DateOnly(1990, 1, 1);
        var priorDoses = new[] { new PriorVaccineDoseAdministered("18", new DateOnly(2021, 1, 1), PriorDoseEvaluationStatus.Valid) };

        // Reference date itself is before the set's effectiveDate (2022-05-06), so the set
        // isn't applicable at all regardless of prior doses.
        var canSkip = EvaluateConditionalSkip.CanBeSkipped(
            dob, new DateOnly(2021, 6, 1), ConditionalSkipContext.Evaluation,
            RabiesRiskDose3, priorDoses, NoCompletedSeriesExpected);

        Assert.False(canSkip);
    }

    [Fact]
    public void VaccineCountCondition_NonMatchingCvx_DoesNotCount()
    {
        var dob = new DateOnly(1990, 1, 1);
        var priorDoses = new[] { new PriorVaccineDoseAdministered("999-not-in-list", new DateOnly(2022, 5, 10), PriorDoseEvaluationStatus.Valid) };

        var canSkip = EvaluateConditionalSkip.CanBeSkipped(
            dob, new DateOnly(2022, 6, 1), ConditionalSkipContext.Evaluation,
            RabiesRiskDose3, priorDoses, NoCompletedSeriesExpected);

        Assert.False(canSkip);
    }

    [Fact]
    public void NoConditionalSkipInstances_CannotBeSkipped()
    {
        var canSkip = EvaluateConditionalSkip.CanBeSkipped(
            new DateOnly(2020, 1, 1), new DateOnly(2025, 1, 1), ConditionalSkipContext.Evaluation,
            Array.Empty<ConditionalSkipInstance>(), Array.Empty<PriorVaccineDoseAdministered>(), NoCompletedSeriesExpected);

        Assert.False(canSkip);
    }

    [Fact]
    public void RealHepBDialysisDose1_CompletedSeriesConditionMet_CanBeSkipped()
    {
        var dob = new DateOnly(2000, 1, 1);

        var canSkip = EvaluateConditionalSkip.CanBeSkipped(
            dob, new DateOnly(2024, 1, 1), ConditionalSkipContext.Evaluation,
            HepBDialysisDose1, Array.Empty<PriorVaccineDoseAdministered>(),
            resolveCompletedSeries: _ => true); // group "1" (Standard) is complete

        Assert.True(canSkip);
    }

    [Fact]
    public void RealHepBDialysisDose1_CompletedSeriesConditionNotMet_CannotBeSkipped()
    {
        var dob = new DateOnly(2000, 1, 1);

        var canSkip = EvaluateConditionalSkip.CanBeSkipped(
            dob, new DateOnly(2024, 1, 1), ConditionalSkipContext.Evaluation,
            HepBDialysisDose1, Array.Empty<PriorVaccineDoseAdministered>(),
            resolveCompletedSeries: _ => false); // group "1" (Standard) is NOT complete

        Assert.False(canSkip);
    }
}
