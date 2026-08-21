namespace Cdsi.Core.Evaluation;

/// <summary>
/// Shared result shape for each of the §6.1-6.9 logical components. Each component
/// independently evaluates one condition and feeds into the §6.10 Table 6-31 aggregator —
/// they don't chain statuses into each other, so this type is intentionally minimal rather
/// than trying to be a full per-dose status (see the README's Ch.6 build notes).
/// </summary>
public sealed class DoseEvaluationOutcome
{
    public required bool IsValid { get; init; }

    /// <summary>Only meaningful when IsValid is false: per §6.4, an invalid-due-to-age dose can additionally be "extraneous" (given too old to ever count) rather than merely invalid.</summary>
    public bool IsExtraneous { get; init; }

    /// <summary>The spec's own evaluation-reason text where one exists (e.g. "Too young", "Grace period", "Too old") — used verbatim rather than an invented rule code, so the audit trail matches the document a clinician or auditor would actually read.</summary>
    public string? Reason { get; init; }

    public static DoseEvaluationOutcome Valid(string? reason = null) => new() { IsValid = true, Reason = reason };
    public static DoseEvaluationOutcome NotValid(string reason, bool isExtraneous = false) =>
        new() { IsValid = false, IsExtraneous = isExtraneous, Reason = reason };
}
