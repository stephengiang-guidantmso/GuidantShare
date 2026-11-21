using System.Net;
using System.Text;
using GuidantShare.Core;
using GuidantShare.ShareFile;
using GuidantShare.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GuidantShare.Tests.SystemTests;

/// <summary>
/// End to end through HTTP: the real app, real database, real encrypted secret store and the REAL ShareFileService,
/// talking to a fake ShareFile REST API. FileSync (the source of the plans) is faked. Only the outside world is mocked.
/// </summary>
[Trait("Category", "System")]
public class FullFlowSystemTests : IDisposable
{
    private readonly FakeShareFileHttp _http = new();
    private readonly FakeFileSync _fileSync = new();
    private readonly TestApp _app;
    public FullFlowSystemTests() =>
        _app = new TestApp(sp => new ShareFileService(sp.GetRequiredService<ISecretStore>(), new HttpClient(_http), NullLogger<ShareFileService>.Instance), null, _fileSync);
    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task From_a_fresh_install_to_a_user_uploading_a_file()
    {
        // ShareFile content the team already has
        _http.AddFolder("fodropcaqh0001", "SFTP Drop CAQH"); _http.AddFile("fodropcaqh0001", "roster.csv", Encoding.UTF8.GetBytes("id\n1\n"));
        _http.AddFolder("fosubdir000001", "2026", "fodropcaqh0001"); _http.AddFile("fosubdir000001", "jan.csv", new byte[] { 1 });
        _http.AddFolder("fouploadcaqh01", "SFTP Uploads CAQH");

        // FileSync's jobs, already grouped into plans and folders
        _fileSync.AddFolder("CAQH", "CAQH", Direction.Download, "fodropcaqh0001", "Shared Folders/SFTP Drop CAQH");
        _fileSync.AddFolder("CAQH", "CAQH", Direction.Upload, "fouploadcaqh01", "Shared Folders/SFTP Uploads/CAQH");

        // 1. a superadmin exists (what `create-superadmin` does) and signs in
        await _app.AddUserAsync("admin@test.local", Roles.SuperAdmin);
        var admin = _app.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await admin.Login("admin@test.local")).Status);
        Assert.Equal("not_configured", (await admin.Get("/health")).Json.GetProperty("sharefile").GetString());

        // 2. saves GuidantShare's own ShareFile app settings, then connects (a wrong code fails first)
        Assert.Equal(HttpStatusCode.OK, (await admin.Post("/api/sharefile/settings", new { subdomain = "Test", clientId = "cid", clientSecret = "very-secret-value", redirectUri = "https://test.local/cb" })).Status);
        Assert.False((await admin.Get("/api/sharefile/status")).Json.GetProperty("connected").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Post("/api/sharefile/exchange", new { code = "wrong" })).Status);
        Assert.Equal(HttpStatusCode.OK, (await admin.Post("/api/sharefile/exchange", new { code = "good-code" })).Status);
        Assert.Equal("connected", (await admin.Get("/health")).Json.GetProperty("sharefile").GetString());

        // 3. invites a teammate, who accepts and signs in; the teammate cannot reach admin or FileSync areas
        var inv = await admin.Post("/api/invites", new { email = "teammate@test.local", role = "User", noEmail = true, emailOnly = false });
        var link = inv.Json.GetProperty("inviteLink").GetString()!; var token = link[(link.IndexOf("token=") + 6)..];
        var user = _app.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await user.Post("/api/auth/accept-invite", new { token, password = TestApp.Password })).Status);
        Assert.Equal(HttpStatusCode.OK, (await user.Login("teammate@test.local")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.Get("/api/filesync/workflows")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.Get("/api/users")).Status);

        // 4. the teammate browses: plan card, folder list, download listing, subfolder
        var plans = (await user.Get("/api/plans")).Json.GetProperty("plans");
        Assert.Equal("CAQH", Assert.Single(plans.EnumerateArray()).GetProperty("name").GetString());
        Assert.True(plans[0].GetProperty("hasUpload").GetBoolean());
        var folders = (await user.Get("/api/plans/caqh")).Json.GetProperty("folders");
        Assert.Equal(new[] { "download-caqh", "upload-caqh" }, folders.EnumerateArray().Select(f => f.GetProperty("key").GetString()));
        var files = await user.Get("/api/plans/caqh/folders/download-caqh/files?refresh=true");
        Assert.Equal("roster.csv", Assert.Single(files.Json.GetProperty("items").EnumerateArray()).GetProperty("name").GetString());
        var sub = Assert.Single(files.Json.GetProperty("folders").EnumerateArray()).GetProperty("id").GetString();
        var inSub = await user.Get($"/api/plans/caqh/folders/download-caqh/files?folder={sub}&refresh=true");
        Assert.Equal("jan.csv", Assert.Single(inSub.Json.GetProperty("items").EnumerateArray()).GetProperty("name").GetString());

        // 5. uploads a file: negotiated, bytes posted, audited, then visible and protected against a second upload
        const string up = "/api/plans/caqh/folders/upload-caqh";
        var payload = Encoding.UTF8.GetBytes("claim,amount\n1,100\n");
        var done = await user.Upload(up, "claims.csv", payload);
        Assert.Equal(HttpStatusCode.OK, done.Status);
        Assert.Equal(payload, _http.LastUploadBytes); Assert.Contains("claims.csv", _http.FileNames("fouploadcaqh01"));
        Assert.Equal("Succeeded", (await user.Get($"/api/uploads/{done.Json.GetProperty("auditId").GetInt32()}")).Json.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await user.Upload(up, "claims.csv", payload)).Status);
        Assert.Equal(HttpStatusCode.OK, (await user.Upload(up, "claims.csv", payload, overwrite: true)).Status);

        // 6. the plan is disabled in FileSync: the teammate no longer sees it
        _fileSync.Plans.Clear();
        Assert.Empty((await user.Get("/api/plans")).Json.GetProperty("plans").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await user.Get("/api/plans/caqh/folders/download-caqh/files")).Status);
    }

    [Fact]
    public async Task The_client_secret_and_refresh_token_are_stored_encrypted()
    {
        await _app.AddUserAsync("admin@test.local", Roles.SuperAdmin);
        var admin = _app.NewClient(); await admin.Login("admin@test.local");
        await admin.Post("/api/sharefile/settings", new { subdomain = "test", clientId = "cid", clientSecret = "very-secret-value", redirectUri = "https://test.local/cb" });
        await admin.Post("/api/sharefile/exchange", new { code = "good-code" });

        var raw = new StringBuilder();
        foreach (var f in Directory.GetFiles(_app.Dir, "app.db*"))
        {
            using var s = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var ms = new MemoryStream(); s.CopyTo(ms); raw.Append(Encoding.Latin1.GetString(ms.ToArray()));
        }
        Assert.DoesNotContain("very-secret-value", raw.ToString());
        Assert.DoesNotContain("refresh-token", raw.ToString());
    }

    [Fact]
    public async Task Users_and_the_sharefile_connection_survive_an_app_restart_and_the_catalog_comes_from_FileSync_again()
    {
        _http.AddFolder("fopersist00001", "Persist");
        _fileSync.AddFolder("Aetna", "Aetna", Direction.Download, "fopersist00001");
        await _app.AddUserAsync("admin@test.local", Roles.SuperAdmin);
        var admin = _app.NewClient(); await admin.Login("admin@test.local");
        await admin.Post("/api/sharefile/settings", new { subdomain = "test", clientId = "cid", clientSecret = "s3cret", redirectUri = "https://test.local/cb" });
        await admin.Post("/api/sharefile/exchange", new { code = "good-code" });

        // stop the app and start a new host over the same data folder = a restart; nothing is lost and ShareFile stays connected
        _app.KeepData = true; _app.Dispose();
        using var second = new TestAppOnDir(_app.Dir, sp => new ShareFileService(sp.GetRequiredService<ISecretStore>(), new HttpClient(_http), NullLogger<ShareFileService>.Instance), _fileSync);
        var admin2 = second.NewClient(); Assert.Equal(HttpStatusCode.OK, (await admin2.Login("admin@test.local")).Status);
        Assert.Equal("Aetna", Assert.Single((await admin2.Get("/api/plans")).Json.GetProperty("plans").EnumerateArray()).GetProperty("name").GetString());
        Assert.Equal("connected", (await admin2.Get("/health")).Json.GetProperty("sharefile").GetString());
        second.Dispose();
        try { Directory.Delete(_app.Dir, true); } catch { }
    }
}
