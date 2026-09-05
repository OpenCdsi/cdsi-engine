using Microsoft.EntityFrameworkCore;
using OpenCdsi.VaxEngine.Mobile.Models;

namespace OpenCdsi.VaxEngine.Mobile.Data;

public class AppDbContext : DbContext
{
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<ImmunizationEvent> ImmunizationEvents => Set<ImmunizationEvent>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Patient>()
            .HasMany(p => p.ImmunizationEvents)
            .WithOne(e => e.Patient)
            .HasForeignKey(e => e.PatientId);

        // Voided doses stay in the table for audit purposes but are excluded
        // from normal queries automatically. Use IgnoreQueryFilters() on the
        // rare screen that needs to show void history.
        modelBuilder.Entity<ImmunizationEvent>()
            .HasQueryFilter(e => !e.IsVoided);
    }
}
