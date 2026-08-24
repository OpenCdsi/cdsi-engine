/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

namespace Cdsi.Core.Evaluation;

/// <summary>
/// The unified evaluation status a target dose can end up with after running the full §6.1-6.10
/// pipeline for one administered-dose-vs-target-dose pairing. This is the type promised in the
/// README as "left to the orchestrator" — now that the orchestrator exists, this is where
/// DoseEvaluationOutcome's Valid/Extraneous/NotValid, DoseAdministeredConditionResult's
/// Sub-standard, and EvaluateConditionalSkip's Skipped all resolve to one real shape.
/// </summary>
public enum EvaluationStatus { Valid, NotValid, Extraneous, SubStandard }

/// <summary>
/// Table 6-1 through 6-31 collectively define target dose status as three-valued: Satisfied,
/// Not Satisfied, or Skipped (Table 6-11's "Skipped" is NOT the same as "Not Satisfied" - it
/// means the target dose no longer needs to be met at all, not that this administered dose
/// failed to meet it).
/// </summary>
public enum TargetDoseStatus { Satisfied, NotSatisfied, Skipped }

/// <summary>The final result of running §6.1-6.10 for one administered-dose-vs-target-dose pairing.</summary>
public sealed class TargetDoseEvaluationResult
{
    public required TargetDoseStatus TargetDoseStatus { get; init; }

    /// <summary>Only meaningful when TargetDoseStatus is Satisfied or NotSatisfied - a Skipped target dose was never actually evaluated against the administered dose's validity, so it has no evaluation status.</summary>
    public EvaluationStatus? EvaluationStatus { get; init; }

    public string? Reason { get; init; }

    public static TargetDoseEvaluationResult Satisfied(string? reason = null) =>
        new() { TargetDoseStatus = TargetDoseStatus.Satisfied, EvaluationStatus = Evaluation.EvaluationStatus.Valid, Reason = reason };

    public static TargetDoseEvaluationResult NotSatisfied(EvaluationStatus status, string reason) =>
        new() { TargetDoseStatus = TargetDoseStatus.NotSatisfied, EvaluationStatus = status, Reason = reason };

    public static TargetDoseEvaluationResult Skipped() =>
        new() { TargetDoseStatus = TargetDoseStatus.Skipped, EvaluationStatus = null };
}
