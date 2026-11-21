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
public record SetPasswordReq(string? Password);
public record SfSettingsReq(string Subdomain, string ClientId, string? ClientSecret, string? RedirectUri);
public record SfCodeReq(string Code);

public static class EndpointsExt
{
    private static readonly ConcurrentDictionary<string, (DateTime at, IReadOnlyList<SfItem> items)> FolderCache = new();
    // planKey/folderKey:shareFileFolderId -> path relative to the folder's root, for subfolders reachable from it
    private static readonly ConcurrentDictionary<string, string> SeenFolders = new();
    private static readonly ConcurrentDictionary<string, DateTime> OAuthStates = new();

    private static IReadOnlyList<string> Errs(IdentityResult r) => r.Errors.Select(e => e.Description).ToList();

    public static void MapEndpoints(this WebApplication app)
    {
        app.MapGet("/health", async (IShareFileService sf, IEmailSender email, IFileSyncClient fs) =>
        {
            string emailState = "ok";
            if (email is GraphEmailSender g && await g.CheckAsync() != null) emailState = "misconfigured";
            var st = await sf.GetStatusAsync();
            string fileSync;
            try { fileSync = (await fs.GetCatalogAsync()).Stale ? "unreachable" : "ok"; }
            catch (FileSyncUnavailableException) { fileSync = "unreachable"; }
            return new { status = "ok", email = emailState, emailMode = email.Mode, fileSync,
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

        // Removes the account. Their upload history stays (it records who uploaded what).
        admin.MapDelete("/users/{id}", async (string id, UserManager<AppUser> um, ClaimsPrincipal me) =>
        {
            var u = await um.FindByIdAsync(id);
            if (u == null) return Results.NotFound();
            if (u.Id == um.GetUserId(me)) return Results.BadRequest(new { error = "You can't remove yourself." });
            var r = await um.DeleteAsync(u);
            return r.Succeeded ? Results.Ok(new { ok = true }) : Results.BadRequest(new { error = string.Join(" ", Errs(r)) });
        });

        // The admin types a new password for the user (it is checked by the same rules as any password) and tells them. Signs them out everywhere.
        admin.MapPost("/users/{id}/password", async (string id, SetPasswordReq req, UserManager<AppUser> um) =>
        {
            var u = await um.FindByIdAsync(id);
            if (u == null) return Results.NotFound();
            var token = await um.GeneratePasswordResetTokenAsync(u);
            var r = await um.ResetPasswordAsync(u, token, req.Password ?? "");
            if (!r.Succeeded) return Results.BadRequest(new { error = string.Join(" ", Errs(r)) });
            await um.UpdateSecurityStampAsync(u);
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

        // ---------- ShareFile connection (SuperAdmin): GuidantShare's own connection, separate from FileSync's
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
                return Results.Redirect("/admin/settings?result=failed");
            try { await sf.ConnectWithCodeAsync(code, (await um.GetUserAsync(me))?.Email ?? "unknown"); return Results.Redirect("/admin/settings?result=connected"); }
            catch { return Results.Redirect("/admin/settings?result=failed"); }
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

        // ---------- FileSync proxy (SuperAdmin): the FileSync screens call these; FileSync itself is never exposed.
        // Only calls in FileSyncProxyRules are forwarded. CSRF is checked for every non-GET by the middleware in Program.cs.
        admin.MapMethods("/filesync/{**path}", new[] { "GET", "POST", "PUT", "DELETE" },
            async (string path, HttpContext ctx, IFileSyncClient fs, AppDbContext db, UserManager<AppUser> um, CancellationToken ct) =>
            {
                if (!FileSyncProxyRules.IsAllowed(ctx.Request.Method, path, out var target))
                    return Results.Json(new { error = "That FileSync call is not allowed." }, statusCode: 403);

                using var req = new HttpRequestMessage(new HttpMethod(ctx.Request.Method), target + ctx.Request.QueryString);
                if (ctx.Request.ContentLength > 0 || ctx.Request.Headers.ContainsKey("Transfer-Encoding"))
                {
                    req.Content = new StreamContent(ctx.Request.Body);
                    if (ctx.Request.ContentType != null) req.Content.Headers.TryAddWithoutValidation("Content-Type", ctx.Request.ContentType);
                }

                HttpResponseMessage resp;
                try { resp = await fs.SendAsync(req, ct); }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
                {
                    return Results.Json(new { error = "FileSync could not be reached.", message = "FileSync could not be reached." }, statusCode: 502);
                }

                using (resp)
                {
                    if (FileSyncProxyRules.ShouldAudit(ctx.Request.Method, path))
                    {
                        // The change is already made in FileSync; a failed audit write must not turn that into an error for the admin
                        try
                        {
                            db.AdminAudits.Add(new AdminAudit { UserId = um.GetUserId(ctx.User), Action = $"{ctx.Request.Method} {path}", Status = (int)resp.StatusCode, CreatedUtc = DateTime.UtcNow });
                            await db.SaveChangesAsync(CancellationToken.None);
                        }
                        catch (Exception ex) { ctx.RequestServices.GetRequiredService<ILogger<AdminAudit>>().LogError(ex, "Could not record the admin audit entry for {Method} {Path}", ctx.Request.Method, path); }
                    }

                    ctx.Response.StatusCode = (int)resp.StatusCode;
                    if (resp.Content.Headers.ContentType != null) ctx.Response.ContentType = resp.Content.Headers.ContentType.ToString();
                    if (resp.Content.Headers.ContentDisposition != null) ctx.Response.Headers.ContentDisposition = resp.Content.Headers.ContentDisposition.ToString();
                    await resp.Content.CopyToAsync(ctx.Response.Body, ct);
                }
                return Results.Empty;
            }).RequireRateLimiting("proxy");

        // ---------- plans, folders, files (signed-in users). The plans and folders come from FileSync's jobs.
        var auth = api.MapGroup("").RequireAuthorization();
        auth.AddEndpointFilter(async (ctx, next) =>
        {
            try { return await next(ctx); }
            catch (FileSyncUnavailableException ex) { return Results.Json(new { error = ex.Message, code = "filesync_unavailable" }, statusCode: 503); }
        });

        auth.MapGet("/plans", async (bool? refresh, IFileSyncClient fs, ClaimsPrincipal me, CancellationToken ct) =>
        {
            var s = await fs.GetCatalogAsync(refresh == true && me.IsInRole(Roles.SuperAdmin), ct);
            return Results.Ok(new
            {
                stale = s.Stale, fetchedUtc = s.FetchedUtc,
                plans = s.Plans.Select(p => new
                {
                    key = p.Key, name = p.Name,
                    hasDownload = p.Folders.Any(f => f.Direction == "Download"),
                    hasUpload = p.Folders.Any(f => f.Direction == "Upload")
                })
            });
        });

        auth.MapGet("/plans/{planKey}", async (string planKey, IFileSyncClient fs, CancellationToken ct) =>
        {
            var s = await fs.GetCatalogAsync(false, ct);
            var plan = FindPlan(s, planKey);
            if (plan == null) return Results.NotFound(new { error = "That health plan isn't available." });
            return Results.Ok(new { key = plan.Key, name = plan.Name, stale = s.Stale, folders = plan.Folders.Select(FolderDto) });
        });

        auth.MapGet("/plans/{planKey}/folders/{folderKey}/files", async (string planKey, string folderKey, string? folder, string? search, int? page, int? pageSize, bool? refresh,
            IFileSyncClient fs, IShareFileService sf, CancellationToken ct) =>
        {
            var s = await fs.GetCatalogAsync(false, ct);
            var plan = FindPlan(s, planKey); var f = FindFolder(plan, folderKey);
            if (plan == null || f == null) return Results.NotFound(new { error = "That folder isn't available." });
            try
            {
                if (!TryResolveFolder(planKey, f, folder, out var target, out var rel)) return Results.BadRequest(new { error = "That folder isn't part of this plan." });
                var (items, at) = await ListCached(sf, target, refresh == true, ct);
                var subfolders = new List<object>();
                foreach (var sub in items.Where(i => i.IsFolder).OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
                {
                    SeenFolders[SeenKey(planKey, f, sub.Id)] = rel == "" ? sub.Name : rel + "/" + sub.Name;
                    if (string.IsNullOrWhiteSpace(search) || sub.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)) subfolders.Add(new { id = sub.Id, name = sub.Name });
                }
                var q = items.Where(i => !i.IsFolder);
                if (!string.IsNullOrWhiteSpace(search)) q = q.Where(i => i.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase));
                var sorted = q.OrderByDescending(i => i.Modified).ThenBy(i => i.Name).ToList();
                var folderLink = await sf.GetWebLinkAsync(target, ct);
                var size = Math.Clamp(pageSize ?? 25, 1, 100); var pg = Math.Max(page ?? 1, 1);
                var pageItems = sorted.Skip((pg - 1) * size).Take(size).ToList();
                return Results.Ok(new { plan = plan.Name, label = f.Label, direction = f.Direction, stale = s.Stale,
                    total = sorted.Count, page = pg, pageSize = size, refreshedUtc = at, folderLink, folders = subfolders, isRoot = rel == "",
                    location = new { shareFilePath = JoinPath(f.ShareFileDisplayPath, rel) },
                    items = pageItems.Select(i => new { id = i.Id, name = i.Name, size = i.Size, modifiedUtc = i.Modified }) });
            }
            catch (ShareFileNotConnectedException ex) { return Results.Json(new { error = ex.Message, code = "disconnected" }, statusCode: 503); }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException) { return Results.Json(new { error = "ShareFile could not be reached: " + ex.Message }, statusCode: 502); }
        });

        auth.MapGet("/plans/{planKey}/folders/{folderKey}/exists", async (string planKey, string folderKey, string name, string? folder,
            IFileSyncClient fs, IShareFileService sf, CancellationToken ct) =>
        {
            var s = await fs.GetCatalogAsync(false, ct);
            var f = FindFolder(FindPlan(s, planKey), folderKey);
            if (f == null) return Results.NotFound();
            if (!TryResolveFolder(planKey, f, folder, out var tgt, out _)) return Results.BadRequest(new { error = "That folder isn't part of this plan." });
            var (items, _) = await ListCached(sf, tgt, true, ct);
            return Results.Ok(new { exists = items.Any(i => !i.IsFolder && i.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) });
        });

        auth.MapPost("/plans/{planKey}/folders/{folderKey}/uploads", async (string planKey, string folderKey, HttpRequest req, AppDbContext db, IFileSyncClient fs, IShareFileService sf,
            UserManager<AppUser> um, ClaimsPrincipal me, IConfiguration cfg, CancellationToken ct) =>
        {
            var s = await fs.GetCatalogAsync(false, ct);
            var plan = FindPlan(s, planKey); var f = FindFolder(plan, folderKey);
            if (plan == null || f == null) return Results.NotFound(new { error = "That folder isn't available." });
            if (f.Direction != "Upload") return Results.BadRequest(new { error = "Uploads aren't available for this folder." });
            var form = await req.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault();
            var overwrite = form["overwrite"] == "true";
            if (!TryResolveFolder(planKey, f, form["folder"].ToString(), out var target, out var rel)) return Results.BadRequest(new { error = "That folder isn't part of this plan." });
            if (file == null || file.Length == 0) return Results.BadRequest(new { error = "Empty files can't be uploaded." });
            var name = SanitizeName(file.FileName);
            if (name == null) return Results.BadRequest(new { error = "That file name isn't allowed." });
            var max = cfg.GetValue<long?>("Upload:MaxBytes") ?? 500L * 1024 * 1024;
            if (file.Length > max) return Results.BadRequest(new { error = $"File is larger than the {max / 1024 / 1024} MB limit." });
            var allowedExt = cfg["Upload:AllowedExtensions"];
            if (!string.IsNullOrWhiteSpace(allowedExt))
            {
                var allowed = allowedExt.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(x => x.TrimStart('.').ToLowerInvariant());
                if (!allowed.Contains(Path.GetExtension(name).TrimStart('.').ToLowerInvariant()))
                    return Results.BadRequest(new { error = $"Only these file types are allowed: {allowedExt}." });
            }
            var uid = um.GetUserId(me)!;
            var audit = new UploadAudit { UserId = uid, PlanName = plan.Name, FolderLabel = f.Label, SharefileFolderId = target,
                FileName = rel == "" ? name : rel + "/" + name, SizeBytes = file.Length, Status = "Started", CreatedUtc = DateTime.UtcNow };
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

    private static CatalogPlanDto? FindPlan(CatalogSnapshot s, string planKey) =>
        s.Plans.FirstOrDefault(p => p.Key.Equals(planKey, StringComparison.OrdinalIgnoreCase));

    private static CatalogFolderDto? FindFolder(CatalogPlanDto? plan, string folderKey) =>
        plan?.Folders.FirstOrDefault(f => f.Key.Equals(folderKey, StringComparison.OrdinalIgnoreCase));

    private static object FolderDto(CatalogFolderDto f) => new { key = f.Key, label = f.Label, direction = f.Direction, shareFilePath = f.ShareFileDisplayPath };

    private static string SeenKey(string planKey, CatalogFolderDto f, string folderId) => $"{planKey.ToLowerInvariant()}/{f.Key}:{folderId}";

    /// <summary>Resolves the folder the user is in. Only the folder's root or folders reached by listing it are allowed.</summary>
    private static bool TryResolveFolder(string planKey, CatalogFolderDto f, string? folder, out string target, out string rel)
    {
        target = f.ShareFileFolderId; rel = "";
        if (string.IsNullOrEmpty(folder) || folder == f.ShareFileFolderId) return true;
        if (!SeenFolders.TryGetValue(SeenKey(planKey, f, folder), out var r)) return false;
        target = folder; rel = r; return true;
    }

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
