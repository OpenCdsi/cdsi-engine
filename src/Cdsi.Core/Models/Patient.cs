namespace Cdsi.Core.Models;

public sealed class Patient
{
    public required string PatientId { get; init; }
    public required DateOnly DateOfBirth { get; init; }
    public Gender Gender { get; init; } = Gender.Unknown;

    /// <summary>Needed by §7.2 Table 7-3's country-of-birth comparison for the immunity birth-date presumption. Null if unknown.</summary>
    public string? CountryOfBirth { get; init; }

    /// <summary>Active patient observations (e.g. a documented risk condition) used by §5.1 indication matching and §5.1.1 immunity evidence.</summary>
    public IReadOnlyList<PatientObservation> ActiveObservations { get; init; } = Array.Empty<PatientObservation>();

    /// <summary>Documented adverse reactions to prior vaccine doses, used by §7.3 Determine Contraindications. Kept separate from ActiveObservations per Table 7-4's own attribute list, though the underlying supporting data doesn't structurally distinguish which codes belong in which bucket - see AntigenContraindicationData's doc comment.</summary>
    public IReadOnlyList<PatientObservation> AdverseReactions { get; init; } = Array.Empty<PatientObservation>();

    /// <summary>
    /// ASSUMPTION — flagged for your review: §5.1 (Table 5-4) requires a three-way answer to
    /// "does the indication describe any active patient observations?" (Yes/No/Unknown), and is
    /// explicit that an inconclusive ("Unknown") answer must NOT be treated the same as "No" —
    /// it should suppress the series and generate a clinician notification instead of silently
    /// treating the risk factor as absent. A real EHR feed rarely distinguishes "confirmed absent"
    /// from "never asked/unknown," so this codebase exposes that distinction explicitly: any
    /// observation code the caller lists here is treated as "Unknown" rather than "No" for
    /// indication matching. Left empty, every indication not in ActiveObservations resolves to a
    /// plain "No" — worth confirming that's the right default for your data source before this
    /// goes anywhere near real patients.
    /// </summary>
    public IReadOnlyList<string> UnresolvedObservationCodes { get; init; } = Array.Empty<string>();
}

/// <summary>An active clinical observation about the patient (e.g. a risk condition, an immunity/history code) referenced by an indication's observationCode.</summary>
public sealed class PatientObservation
{
    public required string Code { get; init; }
    public string? Text { get; init; }
    public DateOnly? ObservationDate { get; init; }
}
