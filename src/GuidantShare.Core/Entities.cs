namespace GuidantShare.Core;

public enum EmailStatus { Pending, Sent, Failed }

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
    /// <summary>Health plan and folder the file went to (from FileSync's catalog at the time).</summary>
    public string? PlanName { get; set; }
    public string? FolderLabel { get; set; }
    public string? SharefileFolderId { get; set; }
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

/// <summary>A SuperAdmin change made through the FileSync proxy (who, what call, result). Request bodies are never stored.</summary>
public class AdminAudit
{
    public int Id { get; set; }
    public string? UserId { get; set; }
    public string Action { get; set; } = "";
    public int Status { get; set; }
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
