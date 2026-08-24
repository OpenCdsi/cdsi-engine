/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.Evaluation;
using Cdsi.Core.ReferenceData;

namespace Cdsi.Core.Pipeline;

/// <summary>
/// §8.8's per-antigen orchestration: runs §8.1-§8.7 (via SelectPrioritizedPatientSeriesForGroup)
/// once per series group for an antigen, then cross-references each group's own prioritized
/// series against its `equivalentSeriesGroups` counterpart to decide which prioritized series
/// are actually "best" for the antigen. Per the chapter's own framing, this can legitimately
/// return more than one series (e.g. a Standard-group series and a Risk-group series both
/// surviving as best, since they're not always substitutes for one another) or none at all.
///
/// Takes every relevant series for ONE antigen across ALL its series groups - the caller is
/// responsible for having already scoped `allMembersForAntigen` to a single antigen (grouping
/// happens internally here by `SeriesGroupInfo.SeriesGroup`, which is only unique WITHIN one
/// antigen's own file - the same group ID string in a different antigen means something
/// unrelated, so mixing series from different antigens into one call here would silently
/// produce nonsense).
/// </summary>
public static class DetermineBestPatientSeriesForAntigen
{
    public static IReadOnlyList<AntigenSeries> Execute(
        IReadOnlyList<SeriesGroupMember> allMembersForAntigen,
        DateOnly dateOfBirth,
        DateOnly assessmentDate)
    {
        var byGroup = allMembersForAntigen.GroupBy(m => m.Series.SeriesGroupInfo.SeriesGroup).ToArray();

        // Compute §8.1-§8.7's prioritized series (and its own forecast) for every group first -
        // §8.8 needs every group's result available before it can cross-reference any of them.
        var prioritizedByGroup = new Dictionary<string, SeriesGroupMember?>();
        foreach (var group in byGroup)
        {
            var groupMembers = group.ToArray();
            var prioritizedSeries = SelectPrioritizedPatientSeriesForGroup.Execute(groupMembers, dateOfBirth, assessmentDate);
            prioritizedByGroup[group.Key] = prioritizedSeries is not null
                ? groupMembers.FirstOrDefault(m => m.Series == prioritizedSeries)
                : null;
        }

        var best = new List<AntigenSeries>();
        foreach (var member in prioritizedByGroup.Values)
        {
            if (member is null)
            {
                continue; // this group had no resolvable prioritized series at all - nothing to evaluate for "best"
            }

            var equivalentGroupId = member.Series.EquivalentSeriesGroup;
            var equivalentMember = equivalentGroupId is not null && prioritizedByGroup.TryGetValue(equivalentGroupId, out var eq) ? eq : null;

            var isComplete = ClassifyScorablePatientSeries.IsCompletePatientSeries(member.Forecast.Status);
            var equivalentGroupHasComplete = equivalentMember is not null && ClassifyScorablePatientSeries.IsCompletePatientSeries(equivalentMember.Forecast.Status);
            var equivalentGroupHasRisk = equivalentMember is not null && equivalentMember.Series.SeriesType == SeriesType.Risk;

            var isBest = DetermineBestPatientSeries.IsBestPatientSeries(isComplete, equivalentGroupHasComplete, member.Series.SeriesType, equivalentGroupHasRisk);
            if (isBest)
            {
                best.Add(member.Series);
            }
        }

        return best;
    }
}
