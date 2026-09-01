/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Core.Evaluation;
using OpenCdsi.VaxEngine.Core.ReferenceData;
using Xunit;

namespace OpenCdsi.VaxEngine.Core.Tests;

public class DetermineBestPatientSeriesTests
{
    // Real data: HepB series group "1" is entirely SeriesType.Standard; group "2" is entirely
    // SeriesType.Risk. HepA has a real Evaluation Only series (used elsewhere in §8.1's tests).
    private static readonly SeriesType RealStandardType =
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("HepB"))
            .Single(s => s.SeriesName == "HepB 3-dose series").SeriesType;

    private static readonly SeriesType RealRiskType =
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("HepB"))
            .Single(s => s.SeriesName == "HepB risk 3-dose series").SeriesType;

    private static readonly SeriesType RealEvaluationOnlyType =
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("HepA"))
            .Single(s => s.SeriesName == "HepA risk Twinrix tertiary 3-dose series").SeriesType;

    [Fact]
    public void Column1_CompleteSeries_IsAlwaysBest_RegardlessOfOtherConditions()
    {
        // Deliberately contradictory other inputs - completion alone should still win.
        var result = DetermineBestPatientSeries.IsBestPatientSeries(
            isCompletePatientSeries: true,
            equivalentGroupHasCompleteSeries: true,
            seriesType: RealEvaluationOnlyType,
            equivalentGroupHasRiskPrioritizedSeries: true);

        Assert.True(result);
    }

    [Fact]
    public void Column2_RiskSeries_NoEquivalentGroupCompletion_IsBest()
    {
        var result = DetermineBestPatientSeries.IsBestPatientSeries(
            isCompletePatientSeries: false,
            equivalentGroupHasCompleteSeries: false,
            seriesType: RealRiskType,
            equivalentGroupHasRiskPrioritizedSeries: false);

        Assert.True(result);
    }

    [Fact]
    public void Column3_StandardSeries_NoEquivalentGroupCompletionOrRisk_IsBest()
    {
        var result = DetermineBestPatientSeries.IsBestPatientSeries(
            isCompletePatientSeries: false,
            equivalentGroupHasCompleteSeries: false,
            seriesType: RealStandardType,
            equivalentGroupHasRiskPrioritizedSeries: false);

        Assert.True(result);
    }

    [Fact]
    public void EquivalentGroupAlreadyComplete_NotBest_EvenIfRiskType()
    {
        // An equivalent group already covers this patient - this series isn't needed.
        var result = DetermineBestPatientSeries.IsBestPatientSeries(
            isCompletePatientSeries: false,
            equivalentGroupHasCompleteSeries: true,
            seriesType: RealRiskType,
            equivalentGroupHasRiskPrioritizedSeries: false);

        Assert.False(result);
    }

    [Fact]
    public void StandardSeries_EquivalentGroupHasRiskPrioritized_NotBest()
    {
        // A Risk series in the equivalent group already provides supplementary protection.
        var result = DetermineBestPatientSeries.IsBestPatientSeries(
            isCompletePatientSeries: false,
            equivalentGroupHasCompleteSeries: false,
            seriesType: RealStandardType,
            equivalentGroupHasRiskPrioritizedSeries: true);

        Assert.False(result);
    }

    [Fact]
    public void EvaluationOnlySeries_NeverBest_EvenWithFavorableOtherConditions()
    {
        var result = DetermineBestPatientSeries.IsBestPatientSeries(
            isCompletePatientSeries: false,
            equivalentGroupHasCompleteSeries: false,
            seriesType: RealEvaluationOnlyType,
            equivalentGroupHasRiskPrioritizedSeries: false);

        Assert.False(result);
    }
}
