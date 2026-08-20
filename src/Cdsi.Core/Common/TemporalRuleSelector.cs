namespace Cdsi.Core.Common;

/// <summary>
/// Implements Logic Spec §3.3 "Selecting Supporting Data" (business rule RELEVANT-1):
/// given a set of time-boxed instances of the same attribute (e.g. two &lt;age&gt; blocks
/// for the same target dose, valid over different calendar windows), select the single
/// instance whose [effectiveDate, cessationDate) window contains the anchor date.
///
/// The anchor date is the date administered when evaluating a dose (Ch.6), or the
/// assessment date when forecasting (Ch.7).
///
/// This is intentionally the ONLY place this selection logic is implemented — Age,
/// Interval, ConditionalSkip, and the CVX-to-antigen association age windows all call
/// through here rather than re-implementing the effective/cessation window check.
/// </summary>
public static class TemporalRuleSelector
{
    private static readonly DateOnly DistantPast = new(1, 1, 1);
    private static readonly DateOnly DistantFuture = DateOnly.MaxValue;

    /// <summary>
    /// Selects the applicable instance for the given anchor date. Throws if none apply —
    /// well-formed supporting data should always have full temporal coverage, so a gap
    /// indicates a data-loading problem worth surfacing loudly rather than silently
    /// falling through.
    /// </summary>
    public static T SelectApplicable<T>(IReadOnlyList<T> instances, DateOnly anchorDate) where T : ITemporallyVersioned
    {
        foreach (var instance in instances)
        {
            var effective = instance.EffectiveDate ?? DistantPast;
            var cessation = instance.CessationDate ?? DistantFuture;
            if (effective <= anchorDate && anchorDate < cessation)
            {
                return instance;
            }
        }

        throw new InvalidOperationException(
            $"No applicable {typeof(T).Name} instance found for anchor date {anchorDate:yyyy-MM-dd}. " +
            "This indicates a gap in the temporal coverage of the supporting data.");
    }

    /// <summary>Same as <see cref="SelectApplicable{T}"/> but returns null instead of throwing when there is no applicable instance (e.g. an attribute that's entirely optional, such as Age on a seriesDose that has none).</summary>
    public static T? SelectApplicableOrDefault<T>(IReadOnlyList<T>? instances, DateOnly anchorDate) where T : class, ITemporallyVersioned
    {
        if (instances is null || instances.Count == 0)
        {
            return null;
        }

        foreach (var instance in instances)
        {
            var effective = instance.EffectiveDate ?? DistantPast;
            var cessation = instance.CessationDate ?? DistantFuture;
            if (effective <= anchorDate && anchorDate < cessation)
            {
                return instance;
            }
        }

        return null;
    }
}
