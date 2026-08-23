using Cdsi.Core.ReferenceData;

namespace Cdsi.Core.Evaluation;

/// <summary>
/// §8.2 Identify One Prioritized Patient Series (Table 8-3). Checks whether a series group's
/// scorable patient series resolve to a single obvious winner WITHOUT needing the full §8.3-8.7
/// scoring system - a shortcut for the common cases (only one scorable series, or a clean
/// complete/in-process/default winner).
/// </summary>
public static class IdentifyOnePrioritizedPatientSeries
{
    /// <returns>The single prioritized series if Table 8-3 resolves one; null if there's no single winner and the full scoring system (§8.3-8.7) is needed instead.</returns>
    public static AntigenSeries? Execute(
        IReadOnlyList<AntigenSeries> scorableSeries,
        AntigenSeries? defaultSeriesInGroup,
        IReadOnlyList<AntigenSeries> completePatientSeriesInGroup,
        IReadOnlyList<AntigenSeries> inProcessPatientSeriesInGroup)
    {
        // Column 1: no scorable series at all - the single default series wins, if there is one.
        if (scorableSeries.Count == 0)
        {
            return defaultSeriesInGroup;
        }

        // Column 2: exactly one scorable series - it wins outright.
        if (scorableSeries.Count == 1)
        {
            return scorableSeries[0];
        }

        // scorableSeries.Count > 1 from here on.

        // Column 3: exactly one complete series among them.
        if (completePatientSeriesInGroup.Count == 1)
        {
            return completePatientSeriesInGroup[0];
        }

        // Column 4: no complete series, but exactly one in-process series.
        if (completePatientSeriesInGroup.Count == 0 && inProcessPatientSeriesInGroup.Count == 1)
        {
            return inProcessPatientSeriesInGroup[0];
        }

        // Column 5: no complete, no in-process, but there's a default series.
        if (completePatientSeriesInGroup.Count == 0 && inProcessPatientSeriesInGroup.Count == 0 && defaultSeriesInGroup is not null)
        {
            return defaultSeriesInGroup;
        }

        // Default: no single prioritized series - the full §8.3-8.7 scoring system is needed.
        return null;
    }
}
