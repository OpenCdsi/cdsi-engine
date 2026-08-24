/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.Evaluation;
using Cdsi.Core.ReferenceData;
using Xunit;

namespace Cdsi.Core.Tests;

public class ForecastConflictEndDateTests
{
    // Real data: MMR (CVX "03") is a conflicting vaccine type for Varicella (CVX "21", the
    // impacted type), conflictEndInterval "28 days" - the same real conflict pair used
    // throughout this project's §6.7 Vaccine Conflict work, now walked forward instead of back.
    private static readonly ScheduleSupportingData Schedule =
        ScheduleSupportingDataLoader.LoadFile(TestPaths.ScheduleFilePath);

    [Fact]
    public void RealMmrVaricellaConflict_PriorMmrDose_PushesConflictEndDateForward()
    {
        var priorDoses = new[] { new PriorVaccineDoseAdministered("03", new DateOnly(2024, 1, 1), PriorDoseEvaluationStatus.Valid) };

        var result = ForecastConflictEndDate.LatestConflictEndDate(
            targetDosePreferableVaccineCvxCodes: new[] { "21" },
            priorDoses, Schedule.ConflictsByImpactedCvx);

        Assert.Equal(new DateOnly(2024, 1, 29), result); // 2024-01-01 + 28 days
    }

    [Fact]
    public void NoConflictingPriorDose_ReturnsNull()
    {
        var priorDoses = new[] { new PriorVaccineDoseAdministered("08", new DateOnly(2024, 1, 1), PriorDoseEvaluationStatus.Valid) }; // HepB, unrelated

        var result = ForecastConflictEndDate.LatestConflictEndDate(
            targetDosePreferableVaccineCvxCodes: new[] { "21" },
            priorDoses, Schedule.ConflictsByImpactedCvx);

        Assert.Null(result);
    }

    [Fact]
    public void NoPriorDosesAtAll_ReturnsNull()
    {
        var result = ForecastConflictEndDate.LatestConflictEndDate(
            targetDosePreferableVaccineCvxCodes: new[] { "21" },
            Array.Empty<PriorVaccineDoseAdministered>(), Schedule.ConflictsByImpactedCvx);

        Assert.Null(result);
    }

    [Fact]
    public void TargetDoseCvxNotInConflictTable_ReturnsNull()
    {
        var priorDoses = new[] { new PriorVaccineDoseAdministered("03", new DateOnly(2024, 1, 1), PriorDoseEvaluationStatus.Valid) };

        var result = ForecastConflictEndDate.LatestConflictEndDate(
            targetDosePreferableVaccineCvxCodes: new[] { "999-not-a-real-vaccine" },
            priorDoses, Schedule.ConflictsByImpactedCvx);

        Assert.Null(result);
    }

    [Fact]
    public void MultiplePriorDoses_TakesTheLatestConflictEndDate()
    {
        var priorDoses = new[]
        {
            new PriorVaccineDoseAdministered("03", new DateOnly(2024, 1, 1), PriorDoseEvaluationStatus.Valid),
            new PriorVaccineDoseAdministered("03", new DateOnly(2024, 6, 1), PriorDoseEvaluationStatus.Valid)
        };

        var result = ForecastConflictEndDate.LatestConflictEndDate(
            targetDosePreferableVaccineCvxCodes: new[] { "21" },
            priorDoses, Schedule.ConflictsByImpactedCvx);

        Assert.Equal(new DateOnly(2024, 6, 29), result); // the later MMR dose's own +28 days wins
    }

    [Fact]
    public void MultipleTargetDosePreferableVaccines_ChecksAllOfThem()
    {
        // Target dose has two preferable vaccine options - one unrelated to any conflict, one
        // (Varicella, "21") that genuinely conflicts with the prior MMR dose.
        var priorDoses = new[] { new PriorVaccineDoseAdministered("03", new DateOnly(2024, 1, 1), PriorDoseEvaluationStatus.Valid) };

        var result = ForecastConflictEndDate.LatestConflictEndDate(
            targetDosePreferableVaccineCvxCodes: new[] { "999-unrelated", "21" },
            priorDoses, Schedule.ConflictsByImpactedCvx);

        Assert.Equal(new DateOnly(2024, 1, 29), result);
    }
}
