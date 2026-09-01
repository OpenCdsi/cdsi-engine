/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

namespace OpenCdsi.VaxEngine.Core.Evaluation;

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
