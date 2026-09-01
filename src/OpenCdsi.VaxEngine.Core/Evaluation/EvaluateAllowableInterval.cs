/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Core.Common;
using OpenCdsi.VaxEngine.Core.ReferenceData;

namespace OpenCdsi.VaxEngine.Core.Evaluation;

/// <summary>
/// §6.6 Evaluate Allowable Interval (Table 6-21).
///
/// Same reference-date-resolution scope note as EvaluatePreferableInterval: this takes an
/// already-resolved reference date; CALCDTINT-1/2 wiring is deferred until evaluation status
/// (§6.10) and inadvertent-vaccine detection (§6.3) exist.
///
/// Table 6-21 is a simpler binary decision than preferable interval's three-way grace-period
/// split, but §6.6's own body text carries an important asymmetry worth re-stating here: a
/// target dose with NO allowable interval rules at all is "not valid" by default — the
/// opposite of preferable interval's "no rules -> valid" — because allowable interval exists
/// specifically to validate a dose, and an empty rule set means that validation can't happen.
/// </summary>
public static class EvaluateAllowableInterval
{
    public static DoseEvaluationOutcome Execute(
        DateOnly dateAdministered,
        IReadOnlyList<AllowableIntervalRule> rules,
        Func<AllowableIntervalRule, DateOnly?> resolveReferenceDate)
    {
        // §6.6: "In cases where a target dose does not specify allowable interval attributes,
        // evaluate allowable interval cannot be used to validate a vaccine dose administered.
        // To avoid a false validation, the allowable interval should be considered 'not valid'."
        if (rules.Count == 0)
        {
            return DoseEvaluationOutcome.NotValid("No allowable interval defined for this target dose");
        }

        foreach (var group in GroupByReferencePoint(rules))
        {
            var applicable = TemporalRuleSelector.SelectApplicable(group, dateAdministered);
            var referenceDate = resolveReferenceDate(applicable);
            var outcome = EvaluateSingleRule(dateAdministered, referenceDate, applicable);
            if (!outcome.IsValid)
            {
                return outcome; // AND semantics, same as preferable interval.
            }
        }

        return DoseEvaluationOutcome.Valid();
    }

    /// <summary>Evaluates a single, already-temporally-resolved rule instance against Table 6-21.</summary>
    public static DoseEvaluationOutcome EvaluateSingleRule(DateOnly dateAdministered, DateOnly? referenceDate, AllowableIntervalRule rule)
    {
        if (referenceDate is null)
        {
            return DoseEvaluationOutcome.Valid();
        }

        // Table 6-20: absolute minimum interval date defaults to 01/01/1900 if empty.
        var absMinIntervalDate = rule.AbsMinInt?.AddTo(referenceDate.Value) ?? new DateOnly(1900, 1, 1);

        if (dateAdministered < absMinIntervalDate)
        {
            return DoseEvaluationOutcome.NotValid("Too soon");
        }
        return DoseEvaluationOutcome.Valid();
    }

    /// <summary>Same grouping rationale as EvaluatePreferableInterval.GroupByReferencePoint — allowable interval only ever has fromPrevious/fromTargetDose reference points (no fromMostRecent/fromRelevantObs, per the XSD), so the group key is narrower.</summary>
    internal static IEnumerable<IReadOnlyList<AllowableIntervalRule>> GroupByReferencePoint(IReadOnlyList<AllowableIntervalRule> rules) =>
        rules.GroupBy(r => (r.ReferenceType, r.ReferenceTargetDoseNumber))
            .Select(g => (IReadOnlyList<AllowableIntervalRule>)g.ToArray());
}
