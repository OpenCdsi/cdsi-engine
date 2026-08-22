namespace Cdsi.Core.Evaluation;

/// <summary>
/// §7.5 FORECASTDN-1: the forecast dose number is (count of satisfied target doses that
/// "count" toward the sequence) + 1. A satisfied target dose counts if its series dose has no
/// seasonal recommendation at all, OR it has one and the administered dose's date falls on/after
/// the seasonal recommendation start date (i.e. a dose given before this year's flu season
/// opened doesn't count toward "how many seasonal doses has this patient had").
/// </summary>
public static class DetermineForecastDoseNumber
{
    public static int Execute(IReadOnlyList<SatisfiedTargetDoseInfo> satisfiedTargetDoses)
    {
        var qualifyingCount = satisfiedTargetDoses.Count(d =>
            d.SeasonalRecommendationStartDate is null || d.DateAdministered >= d.SeasonalRecommendationStartDate.Value);

        return qualifyingCount + 1;
    }
}

/// <summary>The minimal info FORECASTDN-1 needs about one Satisfied target dose - deliberately not the full SeriesHistoryResult/DoseEvaluationRecord shape, so this stays a pure, easily-testable function.</summary>
public sealed record SatisfiedTargetDoseInfo(DateOnly DateAdministered, DateOnly? SeasonalRecommendationStartDate);
