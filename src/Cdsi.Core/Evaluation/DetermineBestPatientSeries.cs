/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.ReferenceData;

namespace Cdsi.Core.Evaluation;

/// <summary>
/// §8.8 Determine Best Patient Series (Table 8-14) - the genuine finale of Chapter 8. Runs once
/// per series group's own prioritized series (the output of §8.7), AFTER every series group for
/// an antigen has already had its own prioritized series selected. Unlike everything earlier in
/// the chapter, this reaches ACROSS series groups via `equivalentSeriesGroups` - "one or more
/// non-redundant best patient series" can survive per antigen, since a Standard-group series and
/// a Risk-group series might BOTH be necessary to fully protect the patient (they're not always
/// substitutes for each other).
///
/// Deliberately a pure function over pre-resolved cross-group facts, not something that itself
/// walks a patient's full set of series groups - that orchestration (compute every group's
/// prioritized series first, then look up each one's equivalent group's status) is a separate,
/// larger piece left for when this gets wired into an end-to-end patient-level flow, same
/// pattern as EvaluateDoseAgainstTargetDose existing before EvaluateSeriesHistory wired multiple
/// dose evaluations together.
/// </summary>
public static class DetermineBestPatientSeries
{
    /// <param name="isCompletePatientSeries">Is THIS group's prioritized series itself complete (SELECTB-6)?</param>
    /// <param name="equivalentGroupHasCompleteSeries">Does an equivalent series group's own prioritized series exist and is complete? False if this series has no equivalent group at all.</param>
    /// <param name="seriesType">This group's prioritized series' own series type.</param>
    /// <param name="equivalentGroupHasRiskPrioritizedSeries">Does an equivalent series group's own prioritized series exist and have series type 'Risk'? False if no equivalent group exists.</param>
    public static bool IsBestPatientSeries(
        bool isCompletePatientSeries,
        bool equivalentGroupHasCompleteSeries,
        SeriesType seriesType,
        bool equivalentGroupHasRiskPrioritizedSeries)
    {
        // Column 1: this series is itself complete - always best, regardless of anything else.
        if (isCompletePatientSeries)
        {
            return true;
        }

        // Column 2: not complete, no equivalent-group completion covers it either, and this
        // series is a Risk series (Risk series don't need to be "complete" to still be the best
        // available protection - they're inherently supplementary).
        if (!equivalentGroupHasCompleteSeries && seriesType == SeriesType.Risk)
        {
            return true;
        }

        // Column 3: not complete, no equivalent-group completion, not Evaluation Only, not Risk
        // (so: Standard), and no equivalent group has its own Risk-type prioritized series either
        // (if one did, that series would already cover the "supplementary protection" role).
        if (!equivalentGroupHasCompleteSeries && seriesType == SeriesType.Standard && !equivalentGroupHasRiskPrioritizedSeries)
        {
            return true;
        }

        // Default: no, this prioritized series is not the best patient series for its group.
        return false;
    }
}
