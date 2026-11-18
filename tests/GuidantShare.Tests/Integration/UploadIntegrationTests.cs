using System.Net;
using System.Text;
using GuidantShare.Core;
using GuidantShare.Tests.Support;

namespace GuidantShare.Tests.Integration;

/// <summary>Uploading through the app, with ShareFile mocked: validation, conflicts, auditing and failures.</summary>
[Trait("Category", "Integration")]
public class UploadIntegrationTests : IAsyncLifetime
{
    private readonly FakeShareFile _sf = new();
    private readonly TestApp _app;
    private ApiClient _user = null!;
    private string _root = "";
    private int _entry;
    public UploadIntegrationTests() => _app = new TestApp(_sf);

    public async Task InitializeAsync()
    {
        await _app.AddUserAsync("user@test.local", Roles.User);
        _user = _app.NewClient(); await _user.Login("user@test.local");
        _root = _sf.AddFolder(name: "SFTP Uploads").Id;
        _entry = await _app.SeedEntryAsync("Aetna", Direction.Upload, _root);
    }
    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);
    private List<GuidantShare.Core.UploadAudit> Audits() => _app.Query(db => db.UploadAudits.OrderBy(a => a.AuditId).ToList());

    [Fact]
    public async Task A_valid_upload_reaches_sharefile_and_is_audited()
    {
        var r = await _user.Upload(_entry, "report.csv", Bytes("a,b\n1,2\n"));
        Assert.Equal(HttpStatusCode.OK, r.Status); Assert.Equal("report.csv", r.Json.GetProperty("name").GetString());
        var up = Assert.Single(_sf.Uploads);
        Assert.Equal(_root, up.FolderId); Assert.Equal("report.csv", up.Name); Assert.Equal("a,b\n1,2\n", Encoding.UTF8.GetString(up.Bytes)); Assert.False(up.Overwrite);
        var audit = Assert.Single(Audits());
        Assert.Equal("Succeeded", audit.Status); Assert.Equal(8, audit.SizeBytes); Assert.Equal("report.csv", audit.FileName); Assert.NotNull(audit.SharefileLink);
    }

    [Fact]
    public async Task Path_tricks_in_the_file_name_are_stripped()
    {
        var r = await _user.Upload(_entry, "..\\..\\evil.csv", Bytes("x"));
        Assert.Equal(HttpStatusCode.OK, r.Status); Assert.Equal("evil.csv", Assert.Single(_sf.Uploads).Name);
    }

    [Fact]
    public async Task An_existing_name_is_a_conflict_until_overwrite_is_chosen()
    {
        _sf.AddFile(_root, "report.csv");
        var conflict = await _user.Upload(_entry, "report.csv", Bytes("new"));
        Assert.Equal(HttpStatusCode.Conflict, conflict.Status); Assert.Equal("exists", conflict.Json.GetProperty("code").GetString());
        Assert.Empty(_sf.Uploads); Assert.Equal("Conflict", Assert.Single(Audits()).Status);

        var ok = await _user.Upload(_entry, "report.csv", Bytes("new"), overwrite: true);
        Assert.Equal(HttpStatusCode.OK, ok.Status); Assert.True(Assert.Single(_sf.Uploads).Overwrite);
    }

    [Fact]
    public async Task Conflicts_are_found_regardless_of_letter_case()
    {
        _sf.AddFile(_root, "Report.CSV");
        Assert.Equal(HttpStatusCode.Conflict, (await _user.Upload(_entry, "report.csv", Bytes("x"))).Status);
    }

    [Fact]
    public async Task Empty_files_are_refused()
    {
        var r = await _user.Upload(_entry, "empty.csv", Array.Empty<byte>());
        Assert.Equal(HttpStatusCode.BadRequest, r.Status); Assert.Empty(_sf.Uploads);
    }

    [Fact]
    public async Task Only_allowed_extensions_are_accepted_when_the_entry_restricts_them()
    {
        var entry = await _app.SeedEntryAsync("Anthem", Direction.Upload, _sf.AddFolder().Id, allowed: "csv, .TXT");
        Assert.Equal(HttpStatusCode.BadRequest, (await _user.Upload(entry, "malware.exe", Bytes("x"))).Status);
        Assert.Equal(HttpStatusCode.OK, (await _user.Upload(entry, "ok.csv", Bytes("x"))).Status);
        Assert.Equal(HttpStatusCode.OK, (await _user.Upload(entry, "OK2.txt", Bytes("x"))).Status);
        Assert.Equal(2, _sf.Uploads.Count);
    }

    [Fact]
    public async Task The_size_limit_of_the_entry_is_enforced()
    {
        var entry = await _app.SeedEntryAsync("Anthem", Direction.Upload, _sf.AddFolder().Id, maxBytes: 10);
        var r = await _user.Upload(entry, "big.csv", new byte[11]);
        Assert.Equal(HttpStatusCode.BadRequest, r.Status); Assert.Contains("larger than", r.Error);
        Assert.Equal(HttpStatusCode.OK, (await _user.Upload(entry, "fits.csv", new byte[10])).Status);
    }

    [Fact]
    public async Task Download_entries_do_not_accept_uploads()
    {
        var entry = await _app.SeedEntryAsync("Anthem", Direction.Download, _sf.AddFolder().Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await _user.Upload(entry, "a.csv", Bytes("x"))).Status);
    }

    [Fact]
    public async Task Unknown_entries_and_other_folders_are_refused()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _user.Upload(9999, "a.csv", Bytes("x"))).Status);
        var elsewhere = _sf.AddFolder(name: "Elsewhere").Id;
        Assert.Equal(HttpStatusCode.BadRequest, (await _user.Upload(_entry, "a.csv", Bytes("x"), folder: elsewhere)).Status);
        Assert.Empty(_sf.Uploads);
    }

    [Fact]
    public async Task Uploading_into_a_subfolder_goes_there_and_the_audit_records_the_path()
    {
        var sub = _sf.AddFolder(name: "2026", parentId: _root);
        await _user.Get($"/api/entries/{_entry}/files?refresh=true");   // listing the root is what makes the subfolder reachable
        var r = await _user.Upload(_entry, "jan.csv", Bytes("x"), folder: sub.Id);
        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.Equal(sub.Id, Assert.Single(_sf.Uploads).FolderId);
        Assert.Equal("2026/jan.csv", Assert.Single(Audits()).FileName);
    }

    [Fact]
    public async Task A_sharefile_failure_is_a_502_and_the_audit_says_failed()
    {
        _sf.FailUploads = true;
        var r = await _user.Upload(_entry, "a.csv", Bytes("x"));
        Assert.Equal(HttpStatusCode.BadGateway, r.Status); Assert.StartsWith("Upload failed", r.Error);
        var audit = Assert.Single(Audits()); Assert.Equal("Failed", audit.Status); Assert.NotNull(audit.Error);
    }

    [Fact]
    public async Task A_disconnected_sharefile_is_a_503()
    {
        _sf.Disconnected = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await _user.Upload(_entry, "a.csv", Bytes("x"))).Status);
    }

    [Fact]
    public async Task A_user_can_read_their_own_upload_receipt_but_not_someone_elses()
    {
        var id = (await _user.Upload(_entry, "a.csv", Bytes("x"))).Json.GetProperty("auditId").GetInt32();
        Assert.Equal("Succeeded", (await _user.Get($"/api/uploads/{id}")).Json.GetProperty("status").GetString());

        await _app.AddUserAsync("other@test.local", Roles.User);
        var other = _app.NewClient(); await other.Login("other@test.local");
        Assert.Equal(HttpStatusCode.NotFound, (await other.Get($"/api/uploads/{id}")).Status);
    }

    [Fact]
    public async Task Uploads_need_a_csrf_token()
    {
        var http = _app.CreateClient();
        var form = new MultipartFormDataContent { { new ByteArrayContent(Bytes("x")), "file", "a.csv" } };
        Assert.NotEqual(HttpStatusCode.OK, (await http.PostAsync($"/api/entries/{_entry}/uploads", form)).StatusCode);
        Assert.Empty(_sf.Uploads);
    }
}
