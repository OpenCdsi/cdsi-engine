namespace OpenCdsi.VaxEngine.Mobile.Models;

public class Patient
{
    // GUID, not an autoincrement int — this device may not be the only
    // source of patient records forever, so ids need to be globally unique
    // without a server round-trip from day one.
    public Guid Id { get; set; } = Guid.NewGuid();

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public Gender Gender { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string FullName => $"{FirstName} {LastName}".Trim();

    // Navigation property — not mapped to a column, EF Core infers the
    // relationship from ImmunizationEvent.PatientId.
    public List<ImmunizationEvent> ImmunizationEvents { get; set; } = new();
}
