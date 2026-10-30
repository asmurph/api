using Microsoft.EntityFrameworkCore;
using PricingApi.Domain;

namespace PricingApi.Data;

public class PricingDbContext(DbContextOptions<PricingDbContext> options) : DbContext(options)
{
    public DbSet<PricingCase> Cases => Set<PricingCase>();
    public DbSet<CaseAttachment> Attachments => Set<CaseAttachment>();
    public DbSet<PricingCaseVersion> CaseVersions => Set<PricingCaseVersion>();
    public DbSet<CaseFileVersion> CaseFileVersions { get; set; }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasSequence<int>("CaseNumbers").StartsAt(1).IncrementsBy(1);

        b.Entity<PricingCase>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.CaseNumber).HasMaxLength(20).IsRequired();
            e.HasIndex(c => c.CaseNumber).IsUnique();
            e.HasIndex(c => new { c.OwnerId, c.Status });

            e.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(c => c.OwnerId).HasMaxLength(64).IsRequired();
            e.Property(c => c.OwnerName).HasMaxLength(200);
            e.Property(c => c.Title).HasMaxLength(200).IsRequired();
            e.Property(c => c.RequestType).HasMaxLength(50);
            e.Property(c => c.WorkType).HasMaxLength(50);
            e.Property(c => c.Priority).HasMaxLength(20);
            e.Property(c => c.RequestSource).HasMaxLength(50);
            e.Property(c => c.CodeMode).HasMaxLength(20);
            e.Property(c => c.Code).HasMaxLength(10);
            e.Property(c => c.Descriptor).HasMaxLength(500);
            e.Property(c => c.Parenthetical).HasMaxLength(500);
            e.Property(c => c.Manufacturer).HasMaxLength(200);
            e.Property(c => c.Product).HasMaxLength(200);
            e.Property(c => c.SiteOfService).HasMaxLength(20);
            e.Property(c => c.Jurisdiction).HasMaxLength(50);
            e.Property(c => c.Assignee).HasMaxLength(100);
            e.Property(c => c.IssueSummary).HasMaxLength(4000);
            e.Property(c => c.ComparatorCodes).HasMaxLength(500);
            e.Property(c => c.SpecialInstructions).HasMaxLength(2000);
            e.Property(c => c.Revision).HasDefaultValue(1);
            e.Property(c => c.RowVersion).IsRowVersion();

            e.OwnsOne(c => c.Routing, r => r.Property(x => x.Rationale).HasMaxLength(2000));
            e.OwnsMany(c => c.WorkPortions, w =>
            {
                w.ToTable("CaseWorkPortions");
                w.WithOwner().HasForeignKey("CaseId");
                w.Property<int>("Id");
                w.HasKey("Id");
                w.Property(x => x.Name).HasMaxLength(100).IsRequired();
                w.Property(x => x.Team).HasMaxLength(50);
            });

            e.HasMany(c => c.Attachments).WithOne().HasForeignKey(a => a.CaseId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PricingCaseVersion>(e =>
        {
            e.HasKey(v => v.Id);
            e.Property(v => v.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(v => v.SavedBy).HasMaxLength(64).IsRequired();
            e.Property(v => v.DataJson).IsRequired();
            e.Property(v => v.AttachmentsJson).IsRequired();
            e.HasIndex(v => new { v.CaseId, v.Revision }).IsUnique();
            e.HasOne<PricingCase>().WithMany().HasForeignKey(v => v.CaseId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CaseAttachment>(e =>
        {
            e.HasKey(a => a.Id);
            e.HasIndex(a => new { a.UploadedBy, a.DraftKey });
            e.Property(a => a.DraftKey).HasMaxLength(64);
            e.Property(a => a.FileName).HasMaxLength(255).IsRequired();
            e.Property(a => a.ContentType).HasMaxLength(150);
            e.Property(a => a.StorageKey).HasMaxLength(200).IsRequired();
            e.Property(a => a.UploadedBy).HasMaxLength(64).IsRequired();
        });
    }
}
