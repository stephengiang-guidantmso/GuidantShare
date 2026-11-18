namespace GuidantShare.Core;

public enum Direction { Download, Upload }
public enum RemoteKind { Sftp, Portal }
public enum EmailStatus { Pending, Sent, Failed }

public class HealthPlan
{
    public int PlanId { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public List<CatalogEntry> Entries { get; set; } = new();
}

public class CatalogEntry
{
    public int EntryId { get; set; }
    public int PlanId { get; set; }
    public HealthPlan? Plan { get; set; }
    public Direction Direction { get; set; }
    public string Label { get; set; } = "";
    public string? SharefileFolderId { get; set; }
    public string SharefileDisplayPath { get; set; } = "";
    public RemoteKind RemoteKind { get; set; }
    public string? RemoteHost { get; set; }
    public int? RemotePort { get; set; }
    public string? RemotePath { get; set; }
    public string? PortalName { get; set; }
    public string? RemoteNote { get; set; }
    public string? RemoteUsername { get; set; }      // login name only; never a password
    public string? AllowedExtensions { get; set; }   // comma separated, null = any
    public long? MaxSizeBytes { get; set; }
    public bool IsActive { get; set; } = true;

    public string RemoteDisplay => RemoteKind == RemoteKind.Sftp
        ? $"{RemoteHost}:{RemotePort} {RemotePath}".Trim()
        : $"{PortalName}{(string.IsNullOrEmpty(RemotePath) ? "" : " " + RemotePath)}".Trim();
}

public class Invite
{
    public int InviteId { get; set; }
    public string Email { get; set; } = "";
    public string Role { get; set; } = Roles.User;
    public string TokenHash { get; set; } = "";
    public string? InvitedByUserId { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public DateTime? AcceptedUtc { get; set; }
    public DateTime? RevokedUtc { get; set; }
    public EmailStatus EmailStatus { get; set; } = EmailStatus.Pending;
    public int EmailAttempts { get; set; }
    public string? LastEmailError { get; set; }
    public DateTime? LastEmailAttemptUtc { get; set; }
}

public class UploadAudit
{
    public int AuditId { get; set; }
    public string UserId { get; set; } = "";
    public int EntryId { get; set; }
    public string FileName { get; set; } = "";
    public long SizeBytes { get; set; }
    public string? SharefileItemId { get; set; }
    public string? SharefileLink { get; set; }
    public string Status { get; set; } = "";
    public string? Error { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public class LoginAudit
{
    public int Id { get; set; }
    public string? UserId { get; set; }
    public string Event { get; set; } = "";
    public string? Ip { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public class AppSecret
{
    public string Key { get; set; } = "";
    public byte[] Value { get; set; } = Array.Empty<byte>();
    public DateTime UpdatedUtc { get; set; }
}

public static class Roles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string User = "User";
}
