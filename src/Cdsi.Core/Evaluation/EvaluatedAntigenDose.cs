namespace Cdsi.Core.Evaluation;

/// <summary>
/// One administered dose's outcome, as already determined by an earlier orchestrator step,
/// carried forward as "history" for evaluating later doses. This is the richer internal type
/// the orchestrator uses; individual §6.5-6.7/§6.2 components still take their own narrower
/// input shapes (PriorVaccineDoseAdministered) - the orchestrator maps down to those as needed.
/// </summary>
public sealed record EvaluatedAntigenDose(
    string Antigen,
    string Cvx,
    DateOnly DateAdministered,
    EvaluationStatus? Status,
    int? SatisfiedTargetDoseNumber);
