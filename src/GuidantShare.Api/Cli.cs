using Microsoft.AspNetCore.Identity;
using GuidantShare.Api.Services;
using GuidantShare.Core;
using GuidantShare.Data;

namespace GuidantShare.Api;

public static class Cli
{
    public static async Task<int> CreateSuperAdminAsync(WebApplication app, string[] args)
    {
        string? Arg(string n) { var i = Array.IndexOf(args, n); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        var email = Arg("--email");
        if (string.IsNullOrWhiteSpace(email)) { Console.Error.WriteLine("Usage: create-superadmin --email you@example.com [--password <min 12 chars>]"); return 1; }
        var password = Arg("--password");
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var existing = await users.FindByEmailAsync(email);
        if (existing != null)
        {
            if (!await users.IsInRoleAsync(existing, Roles.SuperAdmin)) await users.AddToRoleAsync(existing, Roles.SuperAdmin);
            Console.WriteLine($"{email} already exists and is a SuperAdmin.");
            return 0;
        }
        if (password != null)
        {
            var u = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
            var r = await users.CreateAsync(u, password);
            if (!r.Succeeded) { Console.Error.WriteLine(string.Join("; ", r.Errors.Select(e => e.Description))); return 1; }
            await users.AddToRoleAsync(u, Roles.SuperAdmin);
            Console.WriteLine($"SuperAdmin {email} created.");
            return 0;
        }
        // No password: create a one-time set-password link.
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var opts = scope.ServiceProvider.GetRequiredService<EmailOptions>();
        var token = InviteService.NewToken();
        db.Invites.Add(new Invite { Email = email, Role = Roles.SuperAdmin, TokenHash = InviteService.Hash(token), CreatedUtc = DateTime.UtcNow, ExpiresUtc = DateTime.UtcNow.AddHours(72), EmailStatus = EmailStatus.Sent });
        await db.SaveChangesAsync();
        Console.WriteLine($"One-time set-password link (72h): {opts.PublicBaseUrl.TrimEnd('/')}/accept-invite?token={token}");
        return 0;
    }
}
