/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.Evaluation;
using Cdsi.Core.ReferenceData;
using Xunit;

namespace Cdsi.Core.Tests;

public class DetermineForecastNeedTests
{
    private static readonly DateOnly FarFutureMaxAge = new(2999, 12, 31); // effectively "no age limit"

    private static ForecastNeedResult Execute(
        bool notSatisfied = false, bool satisfied = false, bool immune = false, bool contraindicated = false,
        DateOnly? assessmentDate = null, SeasonalRecommendation? seasonal = null,
        DateOnly? maxAgeDate = null, DateOnly? candidateEarliestDate = null) =>
        DetermineForecastNeed.Execute(
            notSatisfied, satisfied, immune, contraindicated,
            assessmentDate ?? new DateOnly(2024, 1, 1), seasonal, maxAgeDate ?? FarFutureMaxAge, candidateEarliestDate);

    [Fact]
    public void Column1_NotSatisfiedDose_NoOtherGates_ShouldForecast_NotComplete()
    {
        var result = Execute(notSatisfied: true);

        Assert.True(result.ShouldForecast);
        Assert.Equal(PatientSeriesStatus.NotComplete, result.PatientSeriesStatus);
    }

    [Fact]
    public void Column2_SatisfiedNotNotSatisfied_Complete()
    {
        var result = Execute(satisfied: true);

        Assert.False(result.ShouldForecast);
        Assert.Equal(PatientSeriesStatus.Complete, result.PatientSeriesStatus);
        Assert.Equal("Patient series is complete", result.Reason);
    }

    [Fact]
    public void Column3_NeitherSatisfiedNorNotSatisfied_NotRecommended()
    {
        var result = Execute();

        Assert.False(result.ShouldForecast);
        Assert.Equal(PatientSeriesStatus.NotRecommended, result.PatientSeriesStatus);
        Assert.Equal("Not recommended at this time due to past immunization history", result.Reason);
    }

    [Fact]
    public void Column4_EvidenceOfImmunity_Immune_DominatesOverNotSatisfied()
    {
        var result = Execute(notSatisfied: true, immune: true);

        Assert.False(result.ShouldForecast);
        Assert.Equal(PatientSeriesStatus.Immune, result.PatientSeriesStatus);
        Assert.Equal("Patient has evidence of immunity", result.Reason);
    }

    [Fact]
    public void Column5_Contraindicated_DominatesOverEverything()
    {
        var result = Execute(notSatisfied: true, immune: true, contraindicated: true);

        Assert.False(result.ShouldForecast);
        Assert.Equal(PatientSeriesStatus.Contraindicated, result.PatientSeriesStatus);
        Assert.Equal("Patient has a contraindication", result.Reason);
    }

    [Fact]
    public void Column6_PastSeasonalEndDate_NotRecommended()
    {
        var seasonal = new SeasonalRecommendation { StartDate = new DateOnly(2023, 7, 1), EndDate = new DateOnly(2024, 6, 30) };
        var result = Execute(notSatisfied: true, assessmentDate: new DateOnly(2024, 8, 1), seasonal: seasonal);

        Assert.False(result.ShouldForecast);
        Assert.Equal(PatientSeriesStatus.NotRecommended, result.PatientSeriesStatus);
        Assert.Equal("Past seasonal recommendation end date", result.Reason);
    }

    [Fact]
    public void WithinSeasonalWindow_StillForecasts()
    {
        var seasonal = new SeasonalRecommendation { StartDate = new DateOnly(2023, 7, 1), EndDate = new DateOnly(2024, 6, 30) };
        var result = Execute(notSatisfied: true, assessmentDate: new DateOnly(2024, 1, 1), seasonal: seasonal);

        Assert.True(result.ShouldForecast);
    }

    [Fact]
    public void RealInfluenzaSeasonalData_PastEndDate_NotRecommended()
    {
        // Real data: Influenza standard series, both doses share seasonalRecommendation
        // startDate 2025-07-01, endDate 2026-06-30.
        var series = AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("Influenza"))
            .Single(s => s.SeriesName == "Influenza standard series");
        var dose1Seasonal = series.SeriesDoses.Single(d => d.DoseNumber == 1).SeasonalRecommendation;
        Assert.NotNull(dose1Seasonal);
        Assert.Equal(new DateOnly(2026, 6, 30), dose1Seasonal!.EffectiveEndDate);

        var result = Execute(notSatisfied: true, assessmentDate: new DateOnly(2026, 8, 1), seasonal: dose1Seasonal);

        Assert.False(result.ShouldForecast);
        Assert.Equal(PatientSeriesStatus.NotRecommended, result.PatientSeriesStatus);
    }

    [Fact]
    public void Column7_AssessmentDateAtOrPastMaxAge_AgedOut()
    {
        var maxAgeDate = new DateOnly(2024, 1, 1);
        var result = Execute(notSatisfied: true, assessmentDate: new DateOnly(2024, 1, 1), maxAgeDate: maxAgeDate);

        Assert.False(result.ShouldForecast);
        Assert.Equal(PatientSeriesStatus.AgedOut, result.PatientSeriesStatus);
        Assert.Equal("Patient has exceeded the maximum age", result.Reason);
    }

    [Fact]
    public void BeforeMaxAge_StillForecasts()
    {
        var result = Execute(notSatisfied: true, assessmentDate: new DateOnly(2023, 12, 31), maxAgeDate: new DateOnly(2024, 1, 1));

        Assert.True(result.ShouldForecast);
    }

    [Fact]
    public void Column8_CandidateEarliestAtOrPastMaxAge_AgedOut()
    {
        var maxAgeDate = new DateOnly(2025, 1, 1);
        var result = Execute(notSatisfied: true, maxAgeDate: maxAgeDate, candidateEarliestDate: new DateOnly(2025, 1, 1));

        Assert.False(result.ShouldForecast);
        Assert.Equal(PatientSeriesStatus.AgedOut, result.PatientSeriesStatus);
        Assert.Equal("Patient is unable to finish the series prior to the maximum age", result.Reason);
    }

    [Fact]
    public void NullCandidateEarliestDate_SkipsThatGate_NotYetBuiltDependency()
    {
        // §7.5 doesn't exist yet - passing null should not trigger a false "Aged Out".
        var result = Execute(notSatisfied: true, maxAgeDate: new DateOnly(2025, 1, 1), candidateEarliestDate: null);

        Assert.True(result.ShouldForecast);
    }
}
