using Cdsi.Core.Evaluation;
using Xunit;

namespace Cdsi.Core.Tests;

public class SatisfyTargetDoseTests
{
    private static readonly DoseEvaluationOutcome ValidAge = DoseEvaluationOutcome.Valid();
    private static readonly DoseEvaluationOutcome ExtraneousAge = DoseEvaluationOutcome.NotValid("Too old", isExtraneous: true);
    private static readonly DoseEvaluationOutcome InvalidAge = DoseEvaluationOutcome.NotValid("Too young");
    private static readonly DoseEvaluationOutcome SatisfiedInterval = DoseEvaluationOutcome.Valid();
    private static readonly DoseEvaluationOutcome UnsatisfiedInterval = DoseEvaluationOutcome.NotValid("Too soon");

    [Fact]
    public void Rule1_EverythingPasses_IsSatisfiedAndValid()
    {
        var result = SatisfyTargetDose.Execute(
            ValidAge, SatisfiedInterval, UnsatisfiedInterval,
            isImpactedByVaccineConflict: false, isPreferableOrAllowableVaccine: true);

        Assert.True(result.IsValid);
        Assert.False(result.IsExtraneous);
    }

    [Fact]
    public void Rule2_AgeExtraneous_ShortCircuitsToNotSatisfiedExtraneous_RegardlessOfOtherConditions()
    {
        // Even with everything else passing, an extraneous age wins.
        var result = SatisfyTargetDose.Execute(
            ExtraneousAge, SatisfiedInterval, SatisfiedInterval,
            isImpactedByVaccineConflict: false, isPreferableOrAllowableVaccine: true);

        Assert.False(result.IsValid);
        Assert.True(result.IsExtraneous);
        Assert.Equal("Too old", result.Reason);
    }

    [Fact]
    public void Rule3_AgeNotValid_IsNotSatisfiedAndNotValid()
    {
        var result = SatisfyTargetDose.Execute(
            InvalidAge, SatisfiedInterval, SatisfiedInterval,
            isImpactedByVaccineConflict: false, isPreferableOrAllowableVaccine: true);

        Assert.False(result.IsValid);
        Assert.False(result.IsExtraneous);
        Assert.Equal("Too young", result.Reason);
    }

    [Fact]
    public void Rule4_NeitherPreferableNorAllowableIntervalSatisfied_IsNotSatisfiedAndNotValid()
    {
        var result = SatisfyTargetDose.Execute(
            ValidAge, UnsatisfiedInterval, UnsatisfiedInterval,
            isImpactedByVaccineConflict: false, isPreferableOrAllowableVaccine: true);

        Assert.False(result.IsValid);
        Assert.False(result.IsExtraneous);
    }

    [Fact]
    public void Rule4_AllowableIntervalAloneSatisfied_StillCountsAsIntervalSatisfied()
    {
        // Table 6-31's exact condition is "ALL preferable intervals OR ALL allowable intervals" -
        // preferable failing while allowable passes should still satisfy the interval condition.
        var result = SatisfyTargetDose.Execute(
            ValidAge, UnsatisfiedInterval, SatisfiedInterval,
            isImpactedByVaccineConflict: false, isPreferableOrAllowableVaccine: true);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Rule5_ImpactedByVaccineConflict_IsNotSatisfiedAndNotValid()
    {
        var result = SatisfyTargetDose.Execute(
            ValidAge, SatisfiedInterval, SatisfiedInterval,
            isImpactedByVaccineConflict: true, isPreferableOrAllowableVaccine: true);

        Assert.False(result.IsValid);
        Assert.False(result.IsExtraneous);
        Assert.Equal("Impacted by vaccine conflict", result.Reason);
    }

    [Fact]
    public void Rule6_NotPreferableOrAllowableVaccine_IsNotSatisfiedAndNotValid()
    {
        var result = SatisfyTargetDose.Execute(
            ValidAge, SatisfiedInterval, SatisfiedInterval,
            isImpactedByVaccineConflict: false, isPreferableOrAllowableVaccine: false);

        Assert.False(result.IsValid);
        Assert.False(result.IsExtraneous);
        Assert.Equal("Not a preferable or allowable vaccine for the target dose", result.Reason);
    }
}
