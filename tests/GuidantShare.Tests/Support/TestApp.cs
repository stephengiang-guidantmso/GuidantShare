using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GuidantShare.Core;
using GuidantShare.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GuidantShare.Tests.Support;

public class CapturingEmail : IEmailSender
{
    public string Mode => "Test";
    public List<(string To, string Subject, string Body)> Sent { get; } = new();
    public Task<EmailResult> SendAsync(string to, string subject, string body, CancellationToken ct = default)
    { lock (Sent) Sent.Add((to, subject, body)); return Task.FromResult(new EmailResult(true, false, null)); }
}

/// <summary>
/// The real app (real routing, auth, CSRF, SQLite in a temp folder) hosted in-process.
/// ShareFile is replaced by whatever the test supplies; email is captured instead of sent.
/// </summary>
public class TestApp : WebApplicationFactory<Program>
{
    protected virtual bool OwnsDir => true;
    public bool KeepData { get; set; }
    public const string Password = "Correct-Horse-Battery-9";
    public string Dir { get; }
    public CapturingEmail Email { get; } = new();
    public FakeFileSync FileSync { get; }
    private readonly Func<IServiceProvider, IShareFileService> _shareFile;
    private readonly Dictionary<string, string?> _oldEnv = new();

    public TestApp(IShareFileService shareFile, FakeFileSync? fileSync = null) : this(_ => shareFile, null, fileSync) { }
    public TestApp(Func<IServiceProvider, IShareFileService> shareFile, string? dir = null, FakeFileSync? fileSync = null)
    {
        Dir = dir ?? Path.Combine(Path.GetTempPath(), "gs-tests-" + Guid.NewGuid().ToString("N"));
        _shareFile = shareFile;
        FileSync = fileSync ?? new FakeFileSync();
        // Program.cs reads these before the host exists, so environment variables are the dependable way in.
        // Tests run one at a time (see AssemblyInfo), so the process-wide variables don't collide.
        Set("Data__Dir", Dir); Set("Data__KeyPath", Path.Combine(Dir, "keys", "master.key"));
        Set("Email__Mode", "Log"); Set("Email__PublicBaseUrl", "http://test.local");
        Set("Serilog__MinimumLevel__Default", "Warning");   // keep test output readable
    }

    private void Set(string k, string v) { _oldEnv[k] = Environment.GetEnvironmentVariable(k); Environment.SetEnvironmentVariable(k, v); }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<IShareFileService>(); s.AddSingleton(_shareFile);
            s.RemoveAll<IFileSyncClient>(); s.AddSingleton<IFileSyncClient>(FileSync);
            s.RemoveAll<IEmailSender>(); s.AddSingleton<IEmailSender>(Email);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        foreach (var (k, v) in _oldEnv) Environment.SetEnvironmentVariable(k, v);
        if (OwnsDir && !KeepData) try { Directory.Delete(Dir, true); } catch { }
    }

    public async Task AddUserAsync(string email, string role)
    {
        using var scope = Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var u = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
        var r = await um.CreateAsync(u, Password);
        Assert.True(r.Succeeded, string.Join(" ", r.Errors.Select(e => e.Description)));
        await um.AddToRoleAsync(u, role);
    }

    /// <summary>Puts a folder in the fake FileSync catalog and returns the URL prefix the app uses for it.</summary>
    public string Seed(string plan, Direction dir, string shareFileFolderId, string label = "Main")
    {
        var f = FileSync.AddFolder(plan, label, dir, shareFileFolderId);
        return $"/api/plans/{FakeFileSync.Slug(plan)}/folders/{f.Key}";
    }

    public T Query<T>(Func<AppDbContext, T> q)
    {
        using var scope = Services.CreateScope();
        return q(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public ApiClient NewClient() => new(this);
}

public record ApiResult(System.Net.HttpStatusCode Status, JsonElement Json)
{
    public bool Ok => (int)Status is >= 200 and < 300;
    public string Error => Json.ValueKind == JsonValueKind.Object && Json.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "";
}

/// <summary>A browser-like client: keeps the cookie jar and attaches a fresh CSRF token to every write, like the UI does.</summary>
public class ApiClient
{
    private readonly HttpClient _http;
    public ApiClient(TestApp app) => _http = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private async Task<string> Csrf() =>
        JsonDocument.Parse(await _http.GetStringAsync("/api/auth/csrf")).RootElement.GetProperty("token").GetString()!;

    private static async Task<ApiResult> Read(HttpResponseMessage r)
    {
        var text = await r.Content.ReadAsStringAsync();
        var json = string.IsNullOrWhiteSpace(text) || !(r.Content.Headers.ContentType?.MediaType ?? "").Contains("json") ? default : JsonDocument.Parse(text).RootElement.Clone();
        return new ApiResult(r.StatusCode, json);
    }

    public async Task<ApiResult> Get(string path) => await Read(await _http.GetAsync(path));

    public async Task<ApiResult> Send(HttpMethod m, string path, object? body = null)
    {
        var req = new HttpRequestMessage(m, path);
        req.Headers.Add("X-CSRF-TOKEN", await Csrf());
        if (body != null) req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return await Read(await _http.SendAsync(req));
    }
    public Task<ApiResult> Post(string path, object? body = null) => Send(HttpMethod.Post, path, body);
    public Task<ApiResult> Put(string path, object body) => Send(HttpMethod.Put, path, body);
    public Task<ApiResult> Patch(string path, object body) => Send(HttpMethod.Patch, path, body);
    public Task<ApiResult> Delete(string path) => Send(HttpMethod.Delete, path);

    public async Task<ApiResult> Login(string email, string password = TestApp.Password) =>
        await Post("/api/auth/login", new { email, password });

    /// <param name="folderUrl">What <see cref="TestApp.Seed"/> returned, e.g. /api/plans/aetna/folders/upload-main</param>
    public async Task<ApiResult> Upload(string folderUrl, string fileName, byte[] bytes, string? folder = null, bool overwrite = false)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes); file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        if (folder != null) form.Add(new StringContent(folder), "folder");
        if (overwrite) form.Add(new StringContent("true"), "overwrite");
        var req = new HttpRequestMessage(HttpMethod.Post, $"{folderUrl}/uploads") { Content = form };
        req.Headers.Add("X-CSRF-TOKEN", await Csrf());
        return await Read(await _http.SendAsync(req));
    }

    public async Task<ApiResult> PostWithoutCsrf(string path, object body) =>
        await Read(await _http.PostAsync(path, new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")));

    public async Task<HttpResponseMessage> Raw(string path) => await _http.GetAsync(path);
}

/// <summary>A second host over an existing data folder, i.e. the app after a restart.</summary>
public class TestAppOnDir : TestApp
{
    public TestAppOnDir(string dir, Func<IServiceProvider, IShareFileService> shareFile, FakeFileSync? fileSync = null) : base(shareFile, dir, fileSync) { }
    protected override bool OwnsDir => false;
}
