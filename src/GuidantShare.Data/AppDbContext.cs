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

    public DbSet<Invite> Invites => Set<Invite>();
    public DbSet<UploadAudit> UploadAudits => Set<UploadAudit>();
    public DbSet<LoginAudit> LoginAudits => Set<LoginAudit>();
    public DbSet<AdminAudit> AdminAudits => Set<AdminAudit>();
    public DbSet<AppSecret> AppSecrets => Set<AppSecret>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<AppSecret>().HasKey(x => x.Key);
        b.Entity<Invite>().HasKey(x => x.InviteId);
        b.Entity<UploadAudit>().HasKey(x => x.AuditId);
        b.Entity<AdminAudit>().HasKey(x => x.Id);
        b.Entity<Invite>().HasIndex(x => x.TokenHash);
        b.Entity<Invite>().Property(x => x.EmailStatus).HasConversion<string>();
    }
}
