/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

namespace Cdsi.Core.Evaluation;

/// <summary>
/// §6.1 Evaluate Dose Administered Condition (Table 6-3) — the first gate in the Chapter 6
/// pipeline, run before Age/Interval/Conflict/Vaccine even apply.
///
/// DELIBERATELY NOT modeled as DoseEvaluationOutcome: Table 6-3 introduces "Sub-standard," a
/// fourth evaluation status distinct from Valid/Not Valid/Extraneous, and this component is a
/// short-circuiting pre-gate rather than one of the AND-able conditions that feed §6.10's
/// aggregator. Forcing it into the same shape as Age/Interval/Conflict/Vaccine would blur a
/// real structural difference. The eventual orchestrator is the right place to unify all of
/// Chapter 6's possible final statuses (Valid/Not Valid/Extraneous/Sub-standard/Skipped) into
/// one type, once it's clear exactly how they need to compose — not guessed at here.
/// </summary>
public static class EvaluateDoseAdministeredCondition
{
    /// <param name="lotExpirationDate">Null if unknown/not tracked - defaults to 12/31/2999 (Table 6-2), meaning "never expired."</param>
    /// <param name="doseConditionFlag">True if the administered dose record carries a condition flag (misadministration, recall, cold chain breach, etc.) per the spec's examples.</param>
    public static DoseAdministeredConditionResult Execute(DateOnly dateAdministered, DateOnly? lotExpirationDate, bool doseConditionFlag)
    {
        var effectiveLotExpirationDate = lotExpirationDate ?? new DateOnly(2999, 12, 31);

        if (dateAdministered > effectiveLotExpirationDate)
        {
            return DoseAdministeredConditionResult.NotEvaluable("Administered after lot expiration date");
        }
        if (doseConditionFlag)
        {
            return DoseAdministeredConditionResult.NotEvaluable("Dose condition flag is set");
        }
        return DoseAdministeredConditionResult.Evaluable();
    }
}

public sealed class DoseAdministeredConditionResult
{
    public required bool CanBeEvaluated { get; init; }

    /// <summary>Only set when CanBeEvaluated is false. Per Table 6-3, both failure rules produce Target Dose Status "Not Satisfied" and Evaluation Status "Sub-standard" - this Reason distinguishes which condition triggered it.</summary>
    public string? Reason { get; init; }

    public static DoseAdministeredConditionResult Evaluable() => new() { CanBeEvaluated = true };
    public static DoseAdministeredConditionResult NotEvaluable(string reason) => new() { CanBeEvaluated = false, Reason = reason };
}
