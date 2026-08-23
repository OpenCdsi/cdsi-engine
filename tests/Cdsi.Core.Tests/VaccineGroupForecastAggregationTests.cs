using Cdsi.Core.Evaluation;
using Xunit;

namespace Cdsi.Core.Tests;

public class VaccineGroupForecastAggregationTests
{
    [Fact]
    public void IsContained_AllThreeConditionsTrue_IsContained()
    {
        var result = VaccineGroupForecastAggregation.IsContainedInVaccineGroupForecast(
            isBestPatientSeries: true, seriesGroupMatches: true, antigenBelongsToVaccineGroup: true);

        Assert.True(result);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void IsContained_AnySingleConditionFalse_IsNotContained(bool isBest, bool groupMatches, bool antigenBelongs)
    {
        var result = VaccineGroupForecastAggregation.IsContainedInVaccineGroupForecast(isBest, groupMatches, antigenBelongs);

        Assert.False(result);
    }

    [Fact]
    public void IsRecommendedAntigen_ContainedAndNotComplete_IsRecommended()
    {
        var result = VaccineGroupForecastAggregation.IsRecommendedAntigen(
            isContainedInVaccineGroupForecast: true, bestPatientSeriesStatus: PatientSeriesStatus.NotComplete);

        Assert.True(result);
    }

    [Fact]
    public void IsRecommendedAntigen_NotContained_NotRecommended_EvenIfNotComplete()
    {
        var result = VaccineGroupForecastAggregation.IsRecommendedAntigen(
            isContainedInVaccineGroupForecast: false, bestPatientSeriesStatus: PatientSeriesStatus.NotComplete);

        Assert.False(result);
    }

    [Theory]
    [InlineData(PatientSeriesStatus.Complete)]
    [InlineData(PatientSeriesStatus.Immune)]
    [InlineData(PatientSeriesStatus.Contraindicated)]
    [InlineData(PatientSeriesStatus.AgedOut)]
    [InlineData(PatientSeriesStatus.NotRecommended)]
    public void IsRecommendedAntigen_ContainedButNotNotComplete_NotRecommended(PatientSeriesStatus status)
    {
        var result = VaccineGroupForecastAggregation.IsRecommendedAntigen(
            isContainedInVaccineGroupForecast: true, bestPatientSeriesStatus: status);

        Assert.False(result);
    }

    [Fact]
    public void RecommendedVaccines_OnlyContainedForecastsContribute()
    {
        var candidates = new (bool, IReadOnlyList<string>)[]
        {
            (true, new[] { "08", "43" }),
            (false, new[] { "999" }), // not contained - should be excluded entirely
            (true, new[] { "121" })
        };

        var result = VaccineGroupForecastAggregation.RecommendedSeriesDoseVaccines(candidates);

        Assert.Equal(new[] { "08", "43", "121" }, result);
        Assert.DoesNotContain("999", result);
    }

    [Fact]
    public void RecommendedVaccines_DeduplicatesAcrossContainedForecasts()
    {
        var candidates = new (bool, IReadOnlyList<string>)[]
        {
            (true, new[] { "08", "43" }),
            (true, new[] { "43", "121" }) // "43" repeated across two contained forecasts
        };

        var result = VaccineGroupForecastAggregation.RecommendedSeriesDoseVaccines(candidates);

        Assert.Equal(new[] { "08", "43", "121" }, result);
    }

    [Fact]
    public void RecommendedVaccines_NoneContained_ReturnsEmpty()
    {
        var candidates = new (bool, IReadOnlyList<string>)[]
        {
            (false, new[] { "08" }),
            (false, new[] { "43" })
        };

        var result = VaccineGroupForecastAggregation.RecommendedSeriesDoseVaccines(candidates);

        Assert.Empty(result);
    }
}
