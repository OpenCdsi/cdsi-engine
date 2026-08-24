/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.ReferenceData;

namespace Cdsi.Core.Pipeline;

/// <summary>
/// §6.2 Table 6-7's "Completed Series" condition, now genuinely resolvable. Builds a lookup of
/// which (antigen, series group) combinations have at least one series whose EVALUATION (§4.4/§6
/// - `SeriesHistoryResult.SeriesComplete`, not §7 Forecast's `PatientSeriesStatus.Complete`) is
/// fully done: every target dose already Satisfied or Skipped. That distinction matters - this
/// condition is evaluated DURING §6's own per-dose walk, before §7 Forecast even runs, so it can
/// only meaningfully mean the pure evaluation concept, not anything requiring immunity/
/// contraindication/age gates that don't exist yet at that point in the pipeline.
///
/// Confirmed against every real instance in the dataset before building this: every real
/// Completed Series condition cross-references a DIFFERENT series group within the SAME antigen
/// (Risk-type series in group "2" checking whether Standard group "1" is already done - e.g.
/// "skip the risk-based HepB Dialysis series if the patient already completed the regular HepB
/// 3-dose series"). Never self-referential in real data. That's what makes a two-pass resolution
/// converge correctly: evaluate once assuming nothing is complete, build this lookup from those
/// results, evaluate again with the real answer available. A genuinely circular reference (two
/// groups each depending on the other's completion) wouldn't fully resolve even with two passes -
/// not something real data exhibits, but worth knowing if that ever changes.
/// </summary>
public static class ResolveCompletedSeriesGroups
{
    public static Func<string, string?, bool> Build(IReadOnlyDictionary<AntigenSeries, SeriesHistoryResult> firstPassResults)
    {
        var completedGroups = firstPassResults
            .Where(kv => kv.Value.SeriesComplete)
            .Select(kv => (kv.Key.Antigen, kv.Key.SeriesGroupInfo.SeriesGroup))
            .ToHashSet();

        return (antigen, seriesGroups) => seriesGroups is not null && completedGroups.Contains((antigen, seriesGroups));
    }
}
