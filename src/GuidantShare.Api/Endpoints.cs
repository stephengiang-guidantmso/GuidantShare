using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using GuidantShare.Api.Services;
using GuidantShare.Core;
using GuidantShare.Data;

namespace GuidantShare.Api;

public record LoginReq(string Email, string Password);
public record AcceptReq(string Token, string Password);
public record InviteReq(string Email, string Role, bool? EmailOnly, bool? NoEmail);
public record UserPatch(bool? IsActive, string? Role);
public record ForgotReq(string Email);
public record ResetReq(string Email, string Token, string Password);
public record SfSettingsReq(string Subdomain, string ClientId, string? ClientSecret, string? RedirectUri);
public record SfCodeReq(string Code);
public record PlanReq(string? Name);
public record EntryReq(int PlanId, string? Direction, string? Label, string? ShareFileLink, string? ShareFilePath, string? RemoteKind,
    string? RemoteHost, int? RemotePort, string? RemotePath, string? PortalName, string? RemoteNote, string? Username);

public static class EndpointsExt
{
    private static readonly ConcurrentDictionary<string, (DateTime at, IReadOnlyList<SfItem> items)> FolderCache = new();
        private static readonly ConcurrentDictionary<string, string> SeenFolders = new();   // entryId:folderId -> path relative to the entry's root, for folders reachable from it
    private static readonly ConcurrentDictionary<string, DateTime> OAuthStates = new();

    private static IReadOnlyList<string> Errs(IdentityResult r) => r.Errors.Select(e => e.Description).ToList();

    public static void MapEndpoints(this WebApplication app)
    {
        app.MapGet("/health", async (IShareFileService sf, IEmailSender email, AppDbContext db) =>
        {
            string emailState = "ok";
            if (email is GraphEmailSender g && await g.CheckAsync() != null) emailState = "misconfigured";
            var st = await sf.GetStatusAsync();
            return new { status = "ok", email = emailState, emailMode = email.Mode,
                sharefile = st.Connected ? "connected" : (st.Configured ? "disconnected" : "not_configured") };
        });

        var api = app.MapGroup("/api");

        // ---------- auth
        api.MapGet("/auth/csrf", (HttpContext ctx, IAntiforgery af) => new { token = af.GetAndStoreTokens(ctx).RequestToken });

        api.MapPost("/auth/login", async (LoginReq req, UserManager<AppUser> um, SignInManager<AppUser> sm, AppDbContext db, HttpContext ctx) =>
        {
            var u = await um.FindByEmailAsync(req.Email ?? "");
            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            const string bad = "Invalid email or password.";
            if (u == null || !u.IsActive) { db.LoginAudits.Add(new LoginAudit { Event = "login_failed", Ip = ip, CreatedUtc = DateTime.UtcNow }); await db.SaveChangesAsync(); return Results.Json(new { error = bad }, statusCode: 401); }
            var r = await sm.PasswordSignInAsync(u, req.Password ?? "", isPersistent: false, lockoutOnFailure: true);
            db.LoginAudits.Add(new LoginAudit { UserId = u.Id, Event = r.Succeeded ? "login" : "login_failed", Ip = ip, CreatedUtc = DateTime.UtcNow });
            if (!r.Succeeded) { await db.SaveChangesAsync(); return Results.Json(new { error = r.IsLockedOut ? "Too many attempts. Try again later." : bad }, statusCode: 401); }
            u.LastLoginUtc = DateTime.UtcNow; await um.UpdateAsync(u);
            return Results.Ok(await Me(um, u));
        }).RequireRateLimiting("auth");

        api.MapPost("/auth/logout", async (SignInManager<AppUser> sm) => { await sm.SignOutAsync(); return Results.Ok(); });

        api.MapGet("/auth/me", async (ClaimsPrincipal p, UserManager<AppUser> um) =>
        {
            var u = await um.GetUserAsync(p);
            return u == null || !u.IsActive ? Results.Unauthorized() : Results.Ok(await Me(um, u));
        }).RequireAuthorization();

        api.MapGet("/auth/invite-info", async (string token, AppDbContext db) =>
        {
            var inv = await FindInvite(db, token);
            return inv == null ? Results.NotFound(new { error = "This invite link is invalid or has expired." }) : Results.Ok(new { email = inv.Email });
        }).RequireRateLimiting("auth");

        api.MapPost("/auth/accept-invite", async (AcceptReq req, AppDbContext db, UserManager<AppUser> um) =>
        {
            var inv = await FindInvite(db, req.Token ?? "");
            if (inv == null) return Results.BadRequest(new { error = "This invite link is invalid or has expired." });
            var u = new AppUser { UserName = inv.Email, Email = inv.Email, EmailConfirmed = true };
            var r = await um.CreateAsync(u, req.Password ?? "");
            if (!r.Succeeded) return Results.BadRequest(new { error = string.Join(" ", Errs(r)) });
            await um.AddToRoleAsync(u, inv.Role);
            inv.AcceptedUtc = DateTime.UtcNow;                // single use
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        }).RequireRateLimiting("auth");

        api.MapPost("/auth/forgot-password", async (ForgotReq req, UserManager<AppUser> um, IEmailSender email, EmailOptions o) =>
        {
            var u = await um.FindByEmailAsync(req.Email ?? "");
            if (u != null && u.IsActive) await SendReset(um, email, o, u);
            return Results.Ok(new { ok = true });             // never reveal whether the email exists
        }).RequireRateLimiting("auth");

        api.MapPost("/auth/reset-password", async (ResetReq req, UserManager<AppUser> um) =>
        {
            var u = await um.FindByEmailAsync(req.Email ?? "");
            if (u == null) return Results.BadRequest(new { error = "This reset link is invalid or has expired." });
            var r = await um.ResetPasswordAsync(u, req.Token ?? "", req.Password ?? "");
            return r.Succeeded ? Results.Ok(new { ok = true }) : Results.BadRequest(new { error = string.Join(" ", Errs(r)) });
        }).RequireRateLimiting("auth");

        // ---------- users & invites (SuperAdmin)
        var admin = api.MapGroup("").RequireAuthorization("SuperAdmin");

        admin.MapGet("/users", async (UserManager<AppUser> um, AppDbContext db) =>
        {
            var list = new List<object>();
            foreach (var u in await um.Users.OrderBy(x => x.Email).ToListAsync())
                list.Add(new { id = u.Id, email = u.Email, role = (await um.GetRolesAsync(u)).FirstOrDefault() ?? Roles.User, isActive = u.IsActive, createdUtc = u.CreatedUtc, lastLoginUtc = u.LastLoginUtc });
            var now = DateTime.UtcNow;
            var invites = await db.Invites.Where(i => i.AcceptedUtc == null && i.RevokedUtc == null).OrderByDescending(i => i.CreatedUtc)
                .Select(i => new { id = i.InviteId, email = i.Email, role = i.Role, emailStatus = i.EmailStatus.ToString(), lastEmailError = i.LastEmailError, createdUtc = i.CreatedUtc, expiresUtc = i.ExpiresUtc, expired = i.ExpiresUtc < now }).ToListAsync();
            return new { users = list, invites };
        });

        admin.MapPatch("/users/{id}", async (string id, UserPatch p, UserManager<AppUser> um, ClaimsPrincipal me) =>
        {
            var u = await um.FindByIdAsync(id);
            if (u == null) return Results.NotFound();
            var isSelf = u.Id == um.GetUserId(me);
            var wasAdmin = await um.IsInRoleAsync(u, Roles.SuperAdmin);
            if (isSelf && (p.IsActive == false || (p.Role != null && p.Role != Roles.SuperAdmin)))
                return Results.BadRequest(new { error = "You can't deactivate or demote yourself." });
            if (p.Role is not null)
            {
                if (p.Role is not (Roles.User or Roles.SuperAdmin)) return Results.BadRequest(new { error = "Invalid role." });
                await um.RemoveFromRolesAsync(u, new[] { Roles.User, Roles.SuperAdmin });
                await um.AddToRoleAsync(u, p.Role);
            }
            if (p.IsActive is not null) { u.IsActive = p.IsActive.Value; await um.UpdateAsync(u); await um.UpdateSecurityStampAsync(u); }
            return Results.Ok(new { ok = true });
        });

        admin.MapPost("/users/{id}/reset-password", async (string id, UserManager<AppUser> um, IEmailSender email, EmailOptions o) =>
        {
            var u = await um.FindByIdAsync(id);
            if (u == null) return Results.NotFound();
            var (res, link) = await SendResetWithLink(um, email, o, u);
            return Results.Ok(new { ok = res.Success, error = res.Error, resetLink = link });   // link is only ever shown to a SuperAdmin
        });

        admin.MapPost("/invites", async (InviteReq req, InviteService svc, UserManager<AppUser> um, ClaimsPrincipal me, CancellationToken ct) =>
        {
            var (r, err) = await svc.CreateAsync(req.Email ?? "", req.Role ?? Roles.User, um.GetUserId(me), sendEmail: req.EmailOnly != false || req.NoEmail != true, ct);
            return r == null ? Results.BadRequest(new { error = err }) : Results.Ok(InviteDto(r, req.EmailOnly == true));
        });
        admin.MapPost("/invites/{id:int}/resend", async (int id, InviteService svc, CancellationToken ct) =>
        {
            var (r, err) = await svc.ResendAsync(id, true, ct);
            return r == null ? Results.BadRequest(new { error = err }) : Results.Ok(InviteDto(r, false));
        });
        admin.MapPost("/invites/{id:int}/revoke", async (int id, AppDbContext db) =>
        {
            var inv = await db.Invites.FindAsync(id);
            if (inv == null) return Results.NotFound();
            inv.RevokedUtc = DateTime.UtcNow; await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        });

        // ---------- ShareFile connection (SuperAdmin): one connection shared by the whole team
        admin.MapGet("/sharefile/status", async (IShareFileService sf) => await sf.GetStatusAsync());
        admin.MapPost("/sharefile/settings", async (SfSettingsReq r, IShareFileService sf) =>
        {
            if (string.IsNullOrWhiteSpace(r.Subdomain) || string.IsNullOrWhiteSpace(r.ClientId)) return Results.BadRequest(new { error = "Subdomain and client id are required." });
            var st = await sf.GetStatusAsync();
            if (!st.Configured && string.IsNullOrWhiteSpace(r.ClientSecret)) return Results.BadRequest(new { error = "Client secret is required the first time." });
            await sf.SaveAppSettingsAsync(r.Subdomain, r.ClientId, r.ClientSecret, r.RedirectUri);
            return Results.Ok(await sf.GetStatusAsync());
        });
        admin.MapPost("/sharefile/connect", async (IShareFileService sf, HttpContext ctx) =>
        {
            var state = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
            foreach (var k in OAuthStates.Where(x => x.Value < DateTime.UtcNow).Select(x => x.Key).ToList()) OAuthStates.TryRemove(k, out _);
            OAuthStates[state] = DateTime.UtcNow.AddMinutes(10);
            try { return Results.Ok(new { url = await sf.BuildAuthorizeUrlAsync(state), state }); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });
        // Landing page when the registered redirect URI points back at this app.
        admin.MapGet("/sharefile/callback", async (string? code, string? state, string? error, IShareFileService sf, UserManager<AppUser> um, ClaimsPrincipal me) =>
        {
            if (error != null || code == null || state == null || !OAuthStates.TryRemove(state, out var exp) || exp < DateTime.UtcNow)
                return Results.Redirect("/admin/sharefile?result=failed");
            try { await sf.ConnectWithCodeAsync(code, (await um.GetUserAsync(me))?.Email ?? "unknown"); return Results.Redirect("/admin/sharefile?result=connected"); }
            catch { return Results.Redirect("/admin/sharefile?result=failed"); }
        });
        // Manual fallback: paste the code (or the whole URL ShareFile sent you to).
        admin.MapPost("/sharefile/exchange", async (SfCodeReq r, IShareFileService sf, UserManager<AppUser> um, ClaimsPrincipal me) =>
        {
            var code = r.Code?.Trim() ?? "";
            if (code.Contains("code=")) { var q = System.Web.HttpUtility.ParseQueryString(new Uri(code).Query); code = q["code"] ?? ""; }
            if (code.Length == 0) return Results.BadRequest(new { error = "Paste the code or the full address you were sent to." });
            try { await sf.ConnectWithCodeAsync(code, (await um.GetUserAsync(me))?.Email ?? "unknown"); return Results.Ok(await sf.GetStatusAsync()); }
            catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
        });
        admin.MapPost("/sharefile/disconnect", async (IShareFileService sf) => { await sf.DisconnectAsync(); return Results.Ok(await sf.GetStatusAsync()); });
        // ---------- catalog management (SuperAdmin). Every entry must carry a ShareFile folder link; passwords are never stored.
        admin.MapGet("/catalog", async (AppDbContext db, IShareFileService sf, CancellationToken ct) =>
        {
            var plans = await db.HealthPlans.Where(p => p.IsActive).Include(p => p.Entries).OrderBy(p => p.SortOrder).ThenBy(p => p.PlanId).ToListAsync(ct);
            var list = new List<object>();
            foreach (var p in plans)
            {
                var entries = new List<object>();
                foreach (var e in p.Entries.Where(e => e.IsActive).OrderBy(e => e.Direction).ThenBy(e => e.EntryId))
                {
                    string? link = null;
                    if (e.SharefileFolderId != null) { try { link = await sf.GetWebLinkAsync(e.SharefileFolderId, ct); } catch { } }
                    entries.Add(new { entryId = e.EntryId, label = e.Label, direction = e.Direction.ToString(), remoteKind = e.RemoteKind.ToString(),
                        remoteHost = e.RemoteHost, remotePort = e.RemotePort, remotePath = e.RemotePath, portalName = e.PortalName, remoteNote = e.RemoteNote,
                        remote = e.RemoteDisplay, username = e.RemoteUsername, shareFilePath = e.SharefileDisplayPath, shareFileLink = link });
                }
                list.Add(new { planId = p.PlanId, name = p.Name, entries });
            }
            return list;
        });
        admin.MapPost("/catalog/plans", async (PlanReq r, AppDbContext db) =>
        {
            var name = r.Name?.Trim() ?? "";
            if (name.Length is 0 or > 100) return Results.BadRequest(new { error = "Enter a health plan name (up to 100 characters)." });
            if (await db.HealthPlans.AnyAsync(p => p.IsActive && p.Name == name)) return Results.BadRequest(new { error = "A health plan with that name already exists." });
            var order = (await db.HealthPlans.MaxAsync(p => (int?)p.SortOrder) ?? 0) + 1;
            var plan = new HealthPlan { Name = name, SortOrder = order };
            db.HealthPlans.Add(plan); await db.SaveChangesAsync();
            return Results.Ok(new { planId = plan.PlanId });
        });
        admin.MapPatch("/catalog/plans/{id:int}", async (int id, PlanReq r, AppDbContext db) =>
        {
            var plan = await db.HealthPlans.FirstOrDefaultAsync(p => p.PlanId == id && p.IsActive);
            if (plan == null) return Results.NotFound();
            var name = r.Name?.Trim() ?? "";
            if (name.Length is 0 or > 100) return Results.BadRequest(new { error = "Enter a health plan name (up to 100 characters)." });
            if (await db.HealthPlans.AnyAsync(p => p.IsActive && p.Name == name && p.PlanId != id)) return Results.BadRequest(new { error = "A health plan with that name already exists." });
            plan.Name = name; await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        });
        admin.MapDelete("/catalog/plans/{id:int}", async (int id, AppDbContext db) =>
        {   // soft delete keeps the upload audit history readable
            var plan = await db.HealthPlans.Include(p => p.Entries).FirstOrDefaultAsync(p => p.PlanId == id && p.IsActive);
            if (plan == null) return Results.NotFound();
            plan.IsActive = false; foreach (var e in plan.Entries) e.IsActive = false;
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        });
        admin.MapPost("/catalog/entries", async (EntryReq r, AppDbContext db, IShareFileService sf, CancellationToken ct) =>
        {
            var plan = await db.HealthPlans.FirstOrDefaultAsync(p => p.PlanId == r.PlanId && p.IsActive, ct);
            if (plan == null) return Results.BadRequest(new { error = "Choose a health plan." });
            var e = new CatalogEntry { PlanId = plan.PlanId };
            if (await ApplyEntryAsync(e, plan.Name, r, sf, ct) is { } err) return Results.BadRequest(new { error = err });
            db.CatalogEntries.Add(e); await db.SaveChangesAsync(ct);
            return Results.Ok(new { entryId = e.EntryId });
        });
        admin.MapPut("/catalog/entries/{id:int}", async (int id, EntryReq r, AppDbContext db, IShareFileService sf, CancellationToken ct) =>
        {
            var e = await db.CatalogEntries.Include(x => x.Plan).FirstOrDefaultAsync(x => x.EntryId == id && x.IsActive, ct);
            if (e == null) return Results.NotFound();
            if (await ApplyEntryAsync(e, e.Plan!.Name, r, sf, ct) is { } err) return Results.BadRequest(new { error = err });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ok = true });
        });
        admin.MapDelete("/catalog/entries/{id:int}", async (int id, AppDbContext db) =>
        {
            var e = await db.CatalogEntries.FirstOrDefaultAsync(x => x.EntryId == id && x.IsActive);
            if (e == null) return Results.NotFound();
            e.IsActive = false; await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        });

        // ---------- catalog
        var auth = api.MapGroup("").RequireAuthorization();
        auth.MapGet("/plans", async (AppDbContext db) =>
        {
            var plans = await db.HealthPlans.Where(p => p.IsActive).Include(p => p.Entries).OrderBy(p => p.SortOrder).ThenBy(p => p.PlanId).ToListAsync();
            return plans.Select(p => new { id = p.PlanId, name = p.Name,
                hasDownload = p.Entries.Any(e => e.IsActive && e.Direction == Direction.Download),
                hasUpload = p.Entries.Any(e => e.IsActive && e.Direction == Direction.Upload) });
        });
        auth.MapGet("/plans/{id:int}/entries", async (int id, string direction, AppDbContext db) =>
        {
            if (!Enum.TryParse<Direction>(direction, true, out var d)) return Results.BadRequest(new { error = "direction must be Download or Upload" });
            var all = await db.CatalogEntries.Where(e => e.PlanId == id && e.Direction == d && e.IsActive).OrderBy(e => e.EntryId).ToListAsync();
            // the same ShareFile folder reached through several accounts is shown once
            return Results.Ok(all.GroupBy(e => e.SharefileFolderId ?? e.SharefileDisplayPath).Select(g => EntryDto(g.First())));
        });
        auth.MapGet("/entries/{id:int}", async (int id, AppDbContext db) =>
            await db.CatalogEntries.Include(e => e.Plan).FirstOrDefaultAsync(e => e.EntryId == id && e.IsActive) is { } e
                ? Results.Ok(new { entry = EntryDto(e), plan = e.Plan!.Name }) : Results.NotFound());

        auth.MapGet("/entries/{id:int}/files", async (int id, string? folder, string? search, int? page, int? pageSize, bool? refresh, AppDbContext db, IShareFileService sf, CancellationToken ct) =>
        {
            var e = await db.CatalogEntries.FirstOrDefaultAsync(x => x.EntryId == id && x.IsActive, ct);
            if (e == null) return Results.NotFound();
            if (e.SharefileFolderId == null) return Results.Json(new { error = "This folder hasn't been linked to ShareFile yet. Ask an admin to add its ShareFile link in Settings." }, statusCode: 503);
            try
            {
                if (!TryResolveFolder(e, folder, out var target, out var rel)) return Results.BadRequest(new { error = "That folder isn't part of this plan." });
                var (items, at) = await ListCached(sf, target, refresh == true, ct);
                var subfolders = new List<object>();
                foreach (var f in items.Where(i => i.IsFolder).OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        SeenFolders[$"{id}:{f.Id}"] = rel == "" ? f.Name : rel + "/" + f.Name;
                        if (string.IsNullOrWhiteSpace(search) || f.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)) subfolders.Add(new { id = f.Id, name = f.Name });
                    }
                var q = items.Where(i => !i.IsFolder);
                if (!string.IsNullOrWhiteSpace(search)) q = q.Where(i => i.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase));
                var sorted = q.OrderByDescending(i => i.Modified).ThenBy(i => i.Name).ToList();
                var folderLink = await sf.GetWebLinkAsync(target, ct);
                var size = Math.Clamp(pageSize ?? 25, 1, 100); var pg = Math.Max(page ?? 1, 1);
                var pageItems = sorted.Skip((pg - 1) * size).Take(size).ToList();
                return Results.Ok(new { total = sorted.Count, page = pg, pageSize = size, refreshedUtc = at, folderLink, folders = subfolders, isRoot = rel == "", location = Location(e, rel),
                    items = pageItems.Select(i => new { id = i.Id, name = i.Name, size = i.Size, modifiedUtc = i.Modified }) });
            }
            catch (ShareFileNotConnectedException ex) { return Results.Json(new { error = ex.Message, code = "disconnected" }, statusCode: 503); }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException) { return Results.Json(new { error = "ShareFile could not be reached: " + ex.Message }, statusCode: 502); }
        });

        auth.MapGet("/entries/{id:int}/exists", async (int id, string name, string? folder, AppDbContext db, IShareFileService sf, CancellationToken ct) =>
        {
            var e = await db.CatalogEntries.FirstOrDefaultAsync(x => x.EntryId == id && x.IsActive, ct);
            if (e?.SharefileFolderId == null) return Results.NotFound();
            if (!TryResolveFolder(e, folder, out var tgt, out _)) return Results.BadRequest(new { error = "That folder isn't part of this plan." });
            var (items, _) = await ListCached(sf, tgt, true, ct);
            return Results.Ok(new { exists = items.Any(i => !i.IsFolder && i.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) });
        });

        auth.MapPost("/entries/{id:int}/uploads", async (int id, HttpRequest req, AppDbContext db, IShareFileService sf, UserManager<AppUser> um, ClaimsPrincipal me, IConfiguration cfg, CancellationToken ct) =>
        {
            var e = await db.CatalogEntries.FirstOrDefaultAsync(x => x.EntryId == id && x.IsActive, ct);
            if (e == null) return Results.NotFound();
            if (e.Direction != Direction.Upload || e.SharefileFolderId == null) return Results.BadRequest(new { error = "Uploads aren't available for this folder." });
            var form = await req.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault();
            var overwrite = form["overwrite"] == "true";
            if (!TryResolveFolder(e, form["folder"].ToString(), out var target, out var rel)) return Results.BadRequest(new { error = "That folder isn't part of this plan." });
            if (file == null || file.Length == 0) return Results.BadRequest(new { error = "Empty files can't be uploaded." });
            var name = SanitizeName(file.FileName);
            if (name == null) return Results.BadRequest(new { error = "That file name isn't allowed." });
            var max = e.MaxSizeBytes ?? cfg.GetValue<long?>("Upload:MaxBytes") ?? 500L * 1024 * 1024;
            if (file.Length > max) return Results.BadRequest(new { error = $"File is larger than the {max / 1024 / 1024} MB limit." });
            if (!string.IsNullOrWhiteSpace(e.AllowedExtensions))
            {
                var allowed = e.AllowedExtensions.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(x => x.TrimStart('.').ToLowerInvariant());
                if (!allowed.Contains(Path.GetExtension(name).TrimStart('.').ToLowerInvariant()))
                    return Results.BadRequest(new { error = $"Only these file types are allowed: {e.AllowedExtensions}." });
            }
            var uid = um.GetUserId(me)!;
            var audit = new UploadAudit { UserId = uid, EntryId = id, FileName = rel == "" ? name : rel + "/" + name, SizeBytes = file.Length, Status = "Started", CreatedUtc = DateTime.UtcNow };
            db.UploadAudits.Add(audit);
            try
            {
                var (existing, _) = await ListCached(sf, target, true, ct);
                if (!overwrite && existing.Any(i => !i.IsFolder && i.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                { audit.Status = "Conflict"; await db.SaveChangesAsync(ct); return Results.Json(new { error = "A file with that name already exists.", code = "exists" }, statusCode: 409); }
                await using var stream = file.OpenReadStream();
                var item = await sf.UploadAsync(target, name, stream, file.Length, overwrite, ct);
                var link = await sf.GetWebLinkAsync(target, ct);
                FolderCache.TryRemove(target, out _);
                audit.Status = "Succeeded"; audit.SharefileItemId = item.Id; audit.SharefileLink = link;
                await db.SaveChangesAsync(ct);
                return Results.Ok(new { id = item.Id, name = item.Name, link, auditId = audit.AuditId });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                audit.Status = "Failed"; audit.Error = ex.Message.Length > 300 ? ex.Message[..300] : ex.Message;
                await db.SaveChangesAsync(CancellationToken.None);
                var code = ex is ShareFileNotConnectedException ? 503 : 502;
                return Results.Json(new { error = ex is ShareFileNotConnectedException ? ex.Message : "Upload failed: " + ex.Message }, statusCode: code);
            }
        }).RequireRateLimiting("upload");

        auth.MapGet("/uploads/{id:int}", async (int id, AppDbContext db, ClaimsPrincipal me, UserManager<AppUser> um) =>
        {
            var a = await db.UploadAudits.FindAsync(id);
            if (a == null || (a.UserId != um.GetUserId(me) && !me.IsInRole(Roles.SuperAdmin))) return Results.NotFound();
            return Results.Ok(new { a.AuditId, a.FileName, a.Status, a.SharefileLink, a.Error, a.CreatedUtc });
        });
    }

    // ---------- helpers
    private static string JoinPath(string baseP, string rel) => string.IsNullOrEmpty(rel) ? baseP : baseP.TrimEnd('/') + "/" + rel;
    private static object Location(CatalogEntry e, string rel) => new
    {
        shareFilePath = JoinPath(e.SharefileDisplayPath, rel),
        remote = rel == "" ? e.RemoteDisplay : (e.RemoteKind == RemoteKind.Sftp
            ? $"{e.RemoteHost}:{e.RemotePort} {JoinPath(e.RemotePath ?? "/", rel)}"
            : $"{e.PortalName} {JoinPath(e.RemotePath ?? "/", rel)}".Trim())
    };
    private static readonly System.Text.RegularExpressions.Regex FolderIdRx = new(@"(?<![0-9A-Za-z_-])(fo[0-9A-Za-z_-]{6,})", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Pulls the ShareFile folder id (starts with "fo") out of a pasted web link; null if there is none.</summary>
    internal static string? ParseFolderId(string? link)
    {
        var m = FolderIdRx.Match(link ?? "");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>Validates the request and fills the entry. The ShareFile link is required and must point at a folder the connected account can open.</summary>
    internal static async Task<string?> ApplyEntryAsync(CatalogEntry e, string planName, EntryReq r, IShareFileService sf, CancellationToken ct)
    {
        if (!Enum.TryParse<Direction>(r.Direction, true, out var dir)) return "Choose Download or Upload.";
        if (!Enum.TryParse<RemoteKind>(r.RemoteKind, true, out var kind)) return "Choose SFTP or Portal.";
        var label = string.IsNullOrWhiteSpace(r.Label) ? planName : r.Label.Trim();   // default label = the health plan name
        if (label.Length > 100) return "The label is too long (up to 100 characters).";
        var link = r.ShareFileLink?.Trim() ?? "";
        if (link.Length == 0) return "The ShareFile folder link is required.";
        var folderId = ParseFolderId(link);
        if (folderId == null) return "That doesn't look like a ShareFile folder link. Open the folder in ShareFile and copy the address (it contains an id starting with “fo”).";
        if (kind == RemoteKind.Sftp && string.IsNullOrWhiteSpace(r.RemoteHost)) return "Enter the SFTP host.";
        if (kind == RemoteKind.Portal && string.IsNullOrWhiteSpace(r.PortalName)) return "Enter the portal name.";
        if (r.RemotePort is < 1 or > 65535) return "The port must be between 1 and 65535.";
        if ((r.Username?.Length ?? 0) > 200) return "Username is too long.";
        SfItem? item;
        try { item = await sf.GetItemAsync(folderId, ct); }
        catch (Exception ex) { return $"Couldn't check the link in ShareFile: {ex.Message}"; }
        if (item == null) return "ShareFile can't find that folder with the connected account. Check the link and that the account has access.";
        if (!item.IsFolder) return "That link points to a file, not a folder.";
        string? T(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        e.Direction = dir; e.Label = label; e.RemoteKind = kind;
        e.SharefileFolderId = item.Id;
        e.SharefileDisplayPath = T(r.ShareFilePath) ?? (dir == Direction.Upload ? $"Shared Folders/SFTP Uploads/{planName}" : $"Shared Folders/SFTP Drop {planName}");
        e.RemoteHost = kind == RemoteKind.Sftp ? T(r.RemoteHost) : null;
        e.RemotePort = kind == RemoteKind.Sftp ? r.RemotePort ?? 22 : null;
        e.RemotePath = T(r.RemotePath);
        e.PortalName = kind == RemoteKind.Portal ? T(r.PortalName) : null;
        e.RemoteNote = T(r.RemoteNote);
        e.RemoteUsername = T(r.Username);
        return null;
    }

    /// <summary>Resolves the folder the user is in. Only the entry root or folders reached by listing it are allowed.</summary>
    private static bool TryResolveFolder(CatalogEntry e, string? folder, out string target, out string rel)
    {
        target = e.SharefileFolderId!; rel = "";
        if (string.IsNullOrEmpty(folder) || folder == e.SharefileFolderId) return true;
        if (!SeenFolders.TryGetValue($"{e.EntryId}:{folder}", out var r)) return false;
        target = folder; rel = r; return true;
    }

    private static object EntryDto(CatalogEntry e) => new
    {
        id = e.EntryId, label = e.Label, direction = e.Direction.ToString(), shareFilePath = e.SharefileDisplayPath,
        remoteKind = e.RemoteKind.ToString(), remote = e.RemoteDisplay, remoteNote = e.RemoteNote, linked = e.SharefileFolderId != null,
        allowedExtensions = e.AllowedExtensions, maxSizeBytes = e.MaxSizeBytes
    };

    private static object InviteDto(InviteResult r, bool emailOnly) => new
    {
        id = r.Invite.InviteId, email = r.Invite.Email, emailStatus = r.Invite.EmailStatus.ToString(), emailError = r.Invite.LastEmailError,
        inviteLink = emailOnly ? null : r.Link   // shown once, only to superadmins
    };

    private static async Task<object> Me(UserManager<AppUser> um, AppUser u) =>
        new { id = u.Id, email = u.Email, role = (await um.GetRolesAsync(u)).FirstOrDefault() ?? Roles.User };

    private static async Task<Invite?> FindInvite(AppDbContext db, string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var h = InviteService.Hash(token);
        var inv = await db.Invites.FirstOrDefaultAsync(i => i.TokenHash == h);
        return inv == null || inv.AcceptedUtc != null || inv.RevokedUtc != null || inv.ExpiresUtc < DateTime.UtcNow ? null : inv;
    }

    private static async Task<EmailResult> SendReset(UserManager<AppUser> um, IEmailSender email, EmailOptions o, AppUser u) => (await SendResetWithLink(um, email, o, u)).Item1;

    private static async Task<(EmailResult, string)> SendResetWithLink(UserManager<AppUser> um, IEmailSender email, EmailOptions o, AppUser u)
    {
        var token = Uri.EscapeDataString(await um.GeneratePasswordResetTokenAsync(u));
        var link = $"{o.PublicBaseUrl.TrimEnd('/')}/reset-password?email={Uri.EscapeDataString(u.Email!)}&token={token}";
        var res = await email.SendAsync(u.Email!, "Reset your GuidantShare password",
            $"Use this link to choose a new password:\n{link}\n\nIf you didn't ask for this, ignore this email.");
        return (res, link);
    }

    private static async Task<(IReadOnlyList<SfItem> items, DateTime at)> ListCached(IShareFileService sf, string folderId, bool force, CancellationToken ct)
    {
        if (!force && FolderCache.TryGetValue(folderId, out var c) && DateTime.UtcNow - c.at < TimeSpan.FromSeconds(30)) return (c.items, c.at);
        var items = await sf.ListChildrenAsync(folderId, ct);
        var at = DateTime.UtcNow;
        FolderCache[folderId] = (at, items);
        return (items, at);
    }

    public static string? SanitizeName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var n = Path.GetFileName(raw.Replace('\\', '/')).Trim();
        if (n.Length == 0 || n.Length > 200 || n is "." or ".." || n.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || n.Any(char.IsControl)) return null;
        return n;
    }
}
