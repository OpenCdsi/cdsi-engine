using Cdsi.Core.Evaluation;
using Cdsi.Core.ReferenceData;
using Xunit;

namespace Cdsi.Core.Tests;

public class EvaluateVaccineConflictTests
{
    private static readonly ScheduleSupportingData Schedule =
        ScheduleSupportingDataLoader.LoadFile(TestPaths.ScheduleFilePath);

    // Real data: MMR (CVX 03) -> MMR (CVX 03) conflict rule -
    // conflictBeginInterval "1 day", minConflictEndInterval "24 days", conflictEndInterval "28 days".
    private const string MmrCvx = "03";

    [Fact]
    public void PriorDoseValid_UsesMinimumEndInterval_ImpactedWithinTheShorterWindow()
    {
        // The exact scenario hand-traced in the design conversation: prior MMR dose Valid,
        // begin = 2024-01-02, end = 2024-01-25 (24 days). 2024-01-20 falls inside.
        var prior = new PriorVaccineDoseAdministered(MmrCvx, new DateOnly(2024, 1, 1), PriorDoseEvaluationStatus.Valid);

        var result = EvaluateVaccineConflict.Execute(
            MmrCvx, new DateOnly(2024, 1, 20), new[] { prior }, Schedule.ConflictsByImpactedCvx);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void PriorDoseValid_AtOrAfterTheShorterEndInterval_IsNotImpacted()
    {
        // 2024-01-26 is past the 24-day minimum end interval (2024-01-25) for a Valid prior dose.
        var prior = new PriorVaccineDoseAdministered(MmrCvx, new DateOnly(2024, 1, 1), PriorDoseEvaluationStatus.Valid);

        var result = EvaluateVaccineConflict.Execute(
            MmrCvx, new DateOnly(2024, 1, 26), new[] { prior }, Schedule.ConflictsByImpactedCvx);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void PriorDoseNotValid_UsesLongerEndInterval_StillImpactedPastTheShorterWindow()
    {
        // Same 2024-01-26 date, but the prior dose was Not Valid this time - CALCDTCONFLICT-2
        // now uses the full 28-day conflictEndInterval (end = 2024-01-29), so 2024-01-26 is
        // still inside the conflict window even though it wasn't for the Valid case above.
        // This is the exact branch distinction Table 6-22/24 exists to capture.
        var prior = new PriorVaccineDoseAdministered(MmrCvx, new DateOnly(2024, 1, 1), PriorDoseEvaluationStatus.NotValid);

        var result = EvaluateVaccineConflict.Execute(
            MmrCvx, new DateOnly(2024, 1, 26), new[] { prior }, Schedule.ConflictsByImpactedCvx);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void PriorDoseWithNoEvaluationStatus_TreatedSameAsValid()
    {
        // CALCDTCONFLICT-2 explicitly groups "no evaluation status" with 'Valid' (both use the
        // minimum end interval) - distinct from 'Not Valid', which alone gets the longer window.
        var prior = new PriorVaccineDoseAdministered(MmrCvx, new DateOnly(2024, 1, 1), EvaluationStatus: null);

        var result = EvaluateVaccineConflict.Execute(
            MmrCvx, new DateOnly(2024, 1, 26), new[] { prior }, Schedule.ConflictsByImpactedCvx);

        Assert.True(result.IsValid); // same outcome as the Valid case, not the NotValid case
    }

    [Fact]
    public void BeforeConflictBeginInterval_IsNotImpacted()
    {
        // conflictBeginInterval is "1 day" - same-day administration (0 days later) is before
        // the window even opens.
        var prior = new PriorVaccineDoseAdministered(MmrCvx, new DateOnly(2024, 1, 1), PriorDoseEvaluationStatus.Valid);

        var result = EvaluateVaccineConflict.Execute(
            MmrCvx, new DateOnly(2024, 1, 1), new[] { prior }, Schedule.ConflictsByImpactedCvx);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void CvxWithNoConflictSupportingData_IsAlwaysValid()
    {
        // §6.7: "if no vaccine Supporting Data exists for the vaccine type... not in conflict
        // with any other vaccine dose administered."
        var result = EvaluateVaccineConflict.Execute(
            "99999-not-a-real-cvx", new DateOnly(2024, 1, 20),
            Array.Empty<PriorVaccineDoseAdministered>(), Schedule.ConflictsByImpactedCvx);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void NoPriorDosesOfAConflictingType_IsValid()
    {
        // Real conflict data exists for MMR, but this patient has no relevant prior doses at all.
        var result = EvaluateVaccineConflict.Execute(
            MmrCvx, new DateOnly(2024, 1, 20),
            Array.Empty<PriorVaccineDoseAdministered>(), Schedule.ConflictsByImpactedCvx);

        Assert.True(result.IsValid);
    }
}
