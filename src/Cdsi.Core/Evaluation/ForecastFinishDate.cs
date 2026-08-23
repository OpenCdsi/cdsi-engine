using Cdsi.Core.ReferenceData;

namespace Cdsi.Core.Evaluation;

/// <summary>
/// SELECTB-12: forecast finish date = the patient series forecast's earliest date PLUS "the
/// latest minimum interval from the remaining target dose(s)."
///
/// IMPLEMENTATION NOTE: DurationExpression values (e.g. "4 weeks" vs "1 month") aren't
/// meaningfully comparable to each other directly - "latest" only makes sense once anchored to
/// a real date, since a month's length varies. This computes it by applying EACH remaining
/// target dose's preferable-interval MinInt duration to the SAME earliestDate anchor, then
/// taking whichever resulting date is latest - mathematically equivalent to "earliest date +
/// the single longest minimum interval among the remaining doses" without ever needing to
/// compare two DurationExpression values against each other in the abstract.
///
/// KNOWN SIMPLIFICATION: this does NOT run TemporalRuleSelector's effective/cessation-date
/// version selection over each target dose's PreferableIntervals - it uses every interval
/// instance present, regardless of which one would actually apply at the anchor date. For a
/// dose with multiple temporally-versioned interval rules (the COVID-19-style case documented
/// elsewhere in this codebase), that could let a superseded rule's duration incorrectly win the
/// MAX if it happens to be longer than the currently-applicable one. Not yet a problem for any
/// real fixture used in this project's tests, but worth fixing before this is relied on for a
/// dose with real temporal interval versioning.
/// </summary>
public static class ForecastFinishDate
{
    public static DateOnly Calculate(DateOnly earliestDate, IReadOnlyList<SeriesDose> remainingTargetDoses)
    {
        var candidateDates = remainingTargetDoses
            .SelectMany(dose => dose.PreferableIntervals)
            .Select(interval => interval.MinInt?.AddTo(earliestDate))
            .Where(date => date is not null)
            .Select(date => date!.Value)
            .ToArray();

        return candidateDates.Length > 0 ? candidateDates.Max() : earliestDate;
    }
}
