/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.Common;
using Cdsi.Core.ReferenceData;

namespace Cdsi.Core.Evaluation;

/// <summary>
/// §6.5 Evaluate Preferable Interval (Table 6-18).
///
/// IMPORTANT SCOPE NOTE: this evaluates interval rules given an ALREADY-RESOLVED reference
/// date. Actually resolving that reference date per CALCDTINT-1/2/8/9 is NOT implemented here
/// on purpose — CALCDTINT-1 (from immediate previous dose) explicitly requires knowing the
/// prior dose's evaluation status ('Valid'/'Not Valid') and whether it was an inadvertent
/// administration, neither of which exist yet (the §6.10 Table 6-31 aggregator and §6.3
/// Evaluate Inadvertent Vaccine haven't been built). Wiring up real resolution belongs there,
/// not here — see the resolveReferenceDate parameter below, which callers (tests, and
/// eventually the real pipeline) supply explicitly.
/// </summary>
public static class EvaluatePreferableInterval
{
    /// <summary>
    /// Evaluates all preferable interval rules for a target dose (§6.5's "all intervals must be
    /// satisfied" AND-across-reference-types rule), each grouped and temporally resolved
    /// first (§3.3 OR-within-reference-type). See the design notes on
    /// <see cref="Cdsi.Core.ReferenceData.PreferableIntervalRule"/> for why these are two
    /// different combination rules on the same XML shape.
    /// </summary>
    public static DoseEvaluationOutcome Execute(
        DateOnly dateAdministered,
        IReadOnlyList<PreferableIntervalRule> rules,
        Func<PreferableIntervalRule, DateOnly?> resolveReferenceDate)
    {
        // §6.5: "In cases where a target dose does not specify preferable interval attributes,
        // the interval is considered 'valid.'"
        if (rules.Count == 0)
        {
            return DoseEvaluationOutcome.Valid();
        }

        foreach (var group in GroupByReferencePoint(rules))
        {
            var applicable = TemporalRuleSelector.SelectApplicable(group, dateAdministered);
            var referenceDate = resolveReferenceDate(applicable);
            var outcome = EvaluateSingleRule(dateAdministered, referenceDate, applicable);
            if (!outcome.IsValid)
            {
                return outcome; // AND semantics: the first failing reference-point group determines the overall outcome.
            }
        }

        return DoseEvaluationOutcome.Valid();
    }

    /// <summary>Evaluates a single, already-temporally-resolved interval rule instance against Table 6-18.</summary>
    public static DoseEvaluationOutcome EvaluateSingleRule(DateOnly dateAdministered, DateOnly? referenceDate, PreferableIntervalRule rule)
    {
        // No resolvable reference event (e.g. "from most recent vaccine type" but none was ever
        // given) means this particular interval constraint doesn't apply — not a failure.
        if (referenceDate is null)
        {
            return DoseEvaluationOutcome.Valid();
        }

        // Table 6-17: both calculated dates default to 01/01/1900 if the attribute is empty,
        // i.e. an unspecified bound imposes no constraint.
        var defaultFloor = new DateOnly(1900, 1, 1);
        var absMinIntervalDate = rule.AbsMinInt?.AddTo(referenceDate.Value) ?? defaultFloor;
        var minIntervalDate = rule.MinInt?.AddTo(referenceDate.Value) ?? defaultFloor;

        if (dateAdministered < absMinIntervalDate)
        {
            return DoseEvaluationOutcome.NotValid("Too soon");
        }
        if (dateAdministered < minIntervalDate)
        {
            return DoseEvaluationOutcome.Valid("Grace period");
        }
        return DoseEvaluationOutcome.Valid();
    }

    /// <summary>
    /// Groups rule instances by reference-point identity (type + target dose number / CVX list /
    /// observation code) so each group can be independently temporally-resolved before the
    /// AND across groups. Different EFFECTIVE/CESSATION windows of the SAME reference point end
    /// up in the same group (OR — pick one); genuinely different reference points end up in
    /// different groups (AND — all must pass).
    /// </summary>
    internal static IEnumerable<IReadOnlyList<PreferableIntervalRule>> GroupByReferencePoint(IReadOnlyList<PreferableIntervalRule> rules) =>
        rules.GroupBy(r => (
                r.ReferenceType,
                r.ReferenceTargetDoseNumber,
                r.ReferenceObservationCode,
                Cvxs: string.Join(",", r.ReferenceVaccineCvxCodes)))
            .Select(g => (IReadOnlyList<PreferableIntervalRule>)g.ToArray());
}
