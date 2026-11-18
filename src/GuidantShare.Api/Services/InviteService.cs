using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using GuidantShare.Core;
using GuidantShare.Data;

namespace GuidantShare.Api.Services;

public record InviteResult(Invite Invite, string Link);

public class InviteService
{
    private readonly AppDbContext _db;
    private readonly IEmailSender _email;
    private readonly EmailOptions _opts;
    private readonly UserManager<AppUser> _users;
    private readonly ILogger<InviteService> _log;
    public static Func<int, TimeSpan> Backoff = n => TimeSpan.FromSeconds(Math.Pow(2, n));

    public InviteService(AppDbContext db, IEmailSender email, EmailOptions opts, UserManager<AppUser> users, ILogger<InviteService> log)
    { _db = db; _email = email; _opts = opts; _users = users; _log = log; }

    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    public async Task<(InviteResult? result, string? error)> CreateAsync(string email, string role, string? invitedBy, bool sendEmail, CancellationToken ct)
    {
        email = email.Trim();
        if (role is not (Roles.User or Roles.SuperAdmin)) return (null, "Invalid role.");
        if (!System.Net.Mail.MailAddress.TryCreate(email, out _)) return (null, "That email address isn't valid.");
        if (await _users.FindByEmailAsync(email) != null) return (null, "A user with that email already exists.");
        var now = DateTime.UtcNow;
        var pending = await _db.Invites.AnyAsync(i => i.Email == email && i.AcceptedUtc == null && i.RevokedUtc == null && i.ExpiresUtc > now, ct);
        if (pending) return (null, "A pending invite already exists for that email. Use Resend instead.");

        var token = NewToken();
        var inv = new Invite { Email = email, Role = role, TokenHash = Hash(token), InvitedByUserId = invitedBy, CreatedUtc = now, ExpiresUtc = now.AddHours(72), EmailStatus = EmailStatus.Pending };
        _db.Invites.Add(inv);
        await _db.SaveChangesAsync(ct);            // saved BEFORE sending, so an email problem never loses the invite
        if (sendEmail) await SendWithRetryAsync(inv, token, ct);
        return (new InviteResult(inv, Link(token)), null);
    }

    public async Task<(InviteResult? result, string? error)> ResendAsync(int id, bool sendEmail, CancellationToken ct)
    {
        var inv = await _db.Invites.FindAsync(new object[] { id }, ct);
        if (inv == null || inv.AcceptedUtc != null) return (null, "Invite not found or already accepted.");
        if (inv.LastEmailAttemptUtc is { } last && DateTime.UtcNow - last < TimeSpan.FromMinutes(1))
            return (null, "Please wait a minute before resending.");
        if (inv.EmailAttempts >= 20) return (null, "Resend limit reached for this invite.");
        var token = NewToken();
        inv.TokenHash = Hash(token); inv.RevokedUtc = null; inv.ExpiresUtc = DateTime.UtcNow.AddHours(72);
        inv.EmailStatus = EmailStatus.Pending; inv.LastEmailError = null;
        await _db.SaveChangesAsync(ct);
        if (sendEmail) await SendWithRetryAsync(inv, token, ct);
        return (new InviteResult(inv, Link(token)), null);
    }

    private string Link(string token) => $"{_opts.PublicBaseUrl.TrimEnd('/')}/accept-invite?token={token}";

    private async Task SendWithRetryAsync(Invite inv, string token, CancellationToken ct)
    {
        var inviter = inv.InvitedByUserId == null ? null : (await _users.FindByIdAsync(inv.InvitedByUserId))?.Email;
        var body = $"{(inviter ?? "An administrator")} invited you to the GuidantShare, a simple tool for finding and sending health plan files.\n\n" +
                   $"Set your password here:\n{Link(token)}\n\nThis link expires in 72 hours and can be used once.\n\n" +
                   "If you weren't expecting this, you can ignore this email.";
        EmailResult res = new(false, false, "Not attempted");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            res = await _email.SendAsync(inv.Email, "You're invited to GuidantShare", body, ct);
            inv.EmailAttempts++; inv.LastEmailAttemptUtc = DateTime.UtcNow;
            if (res.Success || !res.Transient) break;
            if (attempt < 2) await Task.Delay(Backoff(attempt), ct);
        }
        inv.EmailStatus = res.Success ? EmailStatus.Sent : EmailStatus.Failed;
        inv.LastEmailError = res.Success ? null : (res.Transient ? res.Error + " Please resend." : res.Error);
        if (!res.Success && !res.Transient && res.Error?.Contains("allowed to send") == true)
            _log.LogError("Graph mail configuration problem for invite {InviteId}: {Error}", inv.InviteId, res.Error);
        await _db.SaveChangesAsync(ct);
    }
}
