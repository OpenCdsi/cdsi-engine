using Cdsi.Core.Evaluation;
using Cdsi.Core.ReferenceData;
using Xunit;

namespace Cdsi.Core.Tests;

public class ValidateRecommendationTests
{
    // Real data reused from EvaluateConditionalSkipTests: "Hib start at 2 months 4-dose series"
    // Dose 2 has a Forecast-context conditionalSkip instance with beginAge "15 months" exactly
    // (no grace period, unlike the Evaluation-context sibling instance).
    private static IReadOnlyList<ConditionalSkipInstance> HibDose2 =>
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_Hib-508.xml"))
            .Single(s => s.SeriesName == "Hib start at 2 months 4-dose series")
            .SeriesDoses.Single(d => d.DoseNumber == 2).ConditionalSkipInstances;

    private static readonly Func<string?, bool> NoCompletedSeriesExpected =
        _ => throw new InvalidOperationException("Test fixture shouldn't reach a Completed Series condition.");

    [Fact]
    public void ForecastEarliestDateBeforeSkipThreshold_RecommendationIsValid()
    {
        var dob = new DateOnly(2020, 1, 1);
        // Forecast context threshold is exactly "15 months" -> 2021-04-01. A forecast earliest
        // date before that shouldn't be skippable, so the recommendation stays valid.
        var forecastEarliestDate = new DateOnly(2021, 3, 1);

        var isValid = ValidateRecommendation.IsValid(
            dob, forecastEarliestDate, HibDose2, Array.Empty<PriorVaccineDoseAdministered>(), NoCompletedSeriesExpected);

        Assert.True(isValid);
    }

    [Fact]
    public void ForecastEarliestDateAtOrPastSkipThreshold_RecommendationIsInvalid()
    {
        var dob = new DateOnly(2020, 1, 1);
        // By the time the forecast's own earliest date arrives, the target dose would already
        // be skippable under Forecast context - this forecast is stale and needs re-forecasting.
        var forecastEarliestDate = new DateOnly(2021, 4, 1);

        var isValid = ValidateRecommendation.IsValid(
            dob, forecastEarliestDate, HibDose2, Array.Empty<PriorVaccineDoseAdministered>(), NoCompletedSeriesExpected);

        Assert.False(isValid);
    }

    [Fact]
    public void NoConditionalSkipInstances_AlwaysValid()
    {
        var dob = new DateOnly(2020, 1, 1);

        var isValid = ValidateRecommendation.IsValid(
            dob, new DateOnly(2025, 1, 1), Array.Empty<ConditionalSkipInstance>(),
            Array.Empty<PriorVaccineDoseAdministered>(), NoCompletedSeriesExpected);

        Assert.True(isValid);
    }
}
