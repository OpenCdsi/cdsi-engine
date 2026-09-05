namespace OpenCdsi.VaxEngine.Mobile.Models;

public class ImmunizationEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }

    // The two fields vaxengine.core actually needs.
    public string CvxCode { get; set; } = string.Empty;
    public DateOnly DateAdministered { get; set; }

    // Soft delete: a wrong entry gets voided, never removed. Forecasts are
    // computed against history, so silently deleting a dose would change
    // future forecast results with no trace of why.
    public bool IsVoided { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public string? VoidReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Patient? Patient { get; set; }
}
