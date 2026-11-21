using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Serilog;
using GuidantShare.Api;
using GuidantShare.Api.Services;
using GuidantShare.Core;
using GuidantShare.Data;
using GuidantShare.ShareFile;

var builder = WebApplication.CreateBuilder(args);
// Runs as a Windows service in production (no effect when started from a console). Also makes relative paths resolve against the install folder.
builder.Host.UseWindowsService();
var dataDir = Path.GetFullPath(builder.Configuration["Data:Dir"] ?? "data", builder.Environment.ContentRootPath);
Directory.CreateDirectory(dataDir);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console().WriteTo.File(Path.Combine(dataDir, "logs", "wrapper-.log"), rollingInterval: RollingInterval.Day));

var maxUpload = builder.Configuration.GetValue<long?>("Upload:MaxBytes") ?? 500L * 1024 * 1024;
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = maxUpload + 1024 * 1024);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = maxUpload + 1024 * 1024);

builder.Services.AddSingleton(AesGcmCipher.LoadOrCreate(builder.Configuration["Data:KeyPath"] ?? Path.Combine(dataDir, "keys", "master.key")));
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(builder.Configuration["Data:ConnectionString"] ?? $"Data Source={Path.Combine(dataDir, "app.db")}"));
builder.Services.AddSingleton<ISecretStore, SecretStore>();
// GuidantShare has its own ShareFile connection (separate from FileSync's). ShareFile:Mode=Mock is for local development only.
if (string.Equals(builder.Configuration["ShareFile:Mode"], "Mock", StringComparison.OrdinalIgnoreCase))
{
    if (builder.Environment.IsProduction()) throw new InvalidOperationException("ShareFile:Mode=Mock is for development only.");
    builder.Services.AddSingleton<IShareFileService, MockShareFileService>();
}
else
{
    builder.Services.AddSingleton<IShareFileService>(sp => new ShareFileService(
        sp.GetRequiredService<ISecretStore>(), new HttpClient(), sp.GetRequiredService<ILogger<ShareFileService>>()));
}

// FileSync (private, localhost): the source of the health plans and folders, and the target of the SuperAdmin proxy
var fileSyncOpts = builder.Configuration.GetSection("FileSync").Get<FileSyncOptions>() ?? new FileSyncOptions();
builder.Services.AddSingleton(fileSyncOpts);
builder.Services.AddSingleton<IFileSyncClient>(sp => new FileSyncClient(new HttpClient(), fileSyncOpts, sp.GetRequiredService<ILogger<FileSyncClient>>()));

var emailOpts = builder.Configuration.GetSection("Email").Get<EmailOptions>() ?? new EmailOptions();
builder.Services.AddSingleton(emailOpts);
if (emailOpts.Mode.Equals("Graph", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<GraphEmailSender>();
    builder.Services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<GraphEmailSender>());
}
else builder.Services.AddSingleton<IEmailSender, LogEmailSender>();
builder.Services.AddScoped<InviteService>();

builder.Services.AddIdentityCore<AppUser>(o =>
{
    o.Password.RequiredLength = 8; o.Password.RequireDigit = false; o.Password.RequireNonAlphanumeric = false;
    o.Password.RequireUppercase = false; o.Password.RequireLowercase = false;
    o.User.RequireUniqueEmail = true;
    o.Lockout.MaxFailedAccessAttempts = 5; o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
}).AddRoles<IdentityRole>().AddEntityFrameworkStores<AppDbContext>().AddSignInManager()
  .AddDefaultTokenProviders().AddPasswordValidator<CommonPasswordValidator>();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "sfw.auth"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromHours(8); o.SlidingExpiration = true;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization(o => o.AddPolicy("SuperAdmin", p => p.RequireRole(Roles.SuperAdmin)));
builder.Services.AddAntiforgery(o => { o.HeaderName = "X-CSRF-TOKEN"; o.Cookie.Name = "sfw.csrf"; o.Cookie.SameSite = SameSiteMode.Strict; });
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "x",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("upload", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.User.Identity?.Name ?? "x",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));
    // FileSync screens poll (logs, transfers, portal code), so this is generous; it still caps a runaway page or stolen session
    o.AddPolicy("proxy", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.User.Identity?.Name ?? "x",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 600, Window = TimeSpan.FromMinutes(1) }));
});

// Behind the reverse proxy on this machine: trust X-Forwarded-* only from it (loopback is the default trusted proxy),
// so the login rate limit and audit see the real client address
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.ForwardLimit = 1;
});

var app = builder.Build();

// --- DB init (creates a missing database; the roles below always exist)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var r in new[] { Roles.SuperAdmin, Roles.User })
        if (!await roles.RoleExistsAsync(r)) await roles.CreateAsync(new IdentityRole(r));
}

// --- CLI: create-superadmin --email x [--password p]
if (args.Length > 0 && args[0] == "create-superadmin") return await Cli.CreateSuperAdminAsync(app, args);

app.UseForwardedHeaders();
app.UseSerilogRequestLogging();
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff"; h["X-Frame-Options"] = "DENY"; h["Referrer-Policy"] = "no-referrer";
    h["Content-Security-Policy"] = "default-src 'self'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; font-src https://fonts.gstatic.com; img-src 'self' data:; frame-ancestors 'none'";
    await next();
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// CSRF: every state-changing /api call must carry the antiforgery header token.
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method))
    {
        try { await ctx.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(ctx); }
        catch (AntiforgeryValidationException) { ctx.Response.StatusCode = 400; await ctx.Response.WriteAsJsonAsync(new { error = "Invalid CSRF token. Refresh the page." }); return; }
    }
    await next();
});

app.MapEndpoints();
// The API serves no web files: the UI is served by Vite in development and by the reverse proxy in production
app.MapFallback(() => Results.NotFound(new { error = "Not found." }));
await StartupChecks.RunAsync(app);
app.Run();
return 0;

public partial class Program { }
