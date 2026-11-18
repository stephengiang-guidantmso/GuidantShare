using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using GuidantShare.Core;

namespace GuidantShare.Data;

public class AppUser : IdentityUser
{
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginUtc { get; set; }
}

public class AppDbContext : IdentityDbContext<AppUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<HealthPlan> HealthPlans => Set<HealthPlan>();
    public DbSet<CatalogEntry> CatalogEntries => Set<CatalogEntry>();
    public DbSet<Invite> Invites => Set<Invite>();
    public DbSet<UploadAudit> UploadAudits => Set<UploadAudit>();
    public DbSet<LoginAudit> LoginAudits => Set<LoginAudit>();
    public DbSet<AppSecret> AppSecrets => Set<AppSecret>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<AppSecret>().HasKey(x => x.Key);
        b.Entity<HealthPlan>().HasKey(x => x.PlanId);
        b.Entity<CatalogEntry>().HasKey(x => x.EntryId);
        b.Entity<Invite>().HasKey(x => x.InviteId);
        b.Entity<UploadAudit>().HasKey(x => x.AuditId);
        b.Entity<Invite>().HasIndex(x => x.TokenHash);
        b.Entity<Invite>().Property(x => x.EmailStatus).HasConversion<string>();
        b.Entity<CatalogEntry>().Property(x => x.Direction).HasConversion<string>();
        b.Entity<CatalogEntry>().Property(x => x.RemoteKind).HasConversion<string>();
        b.Entity<CatalogEntry>().Ignore(x => x.RemoteDisplay);
        b.Entity<HealthPlan>().HasMany(p => p.Entries).WithOne(e => e.Plan).HasForeignKey(e => e.PlanId);
    }
}
