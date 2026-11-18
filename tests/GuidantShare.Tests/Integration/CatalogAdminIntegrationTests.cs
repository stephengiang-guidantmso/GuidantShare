using System.Net;
using GuidantShare.Core;
using GuidantShare.Tests.Support;

namespace GuidantShare.Tests.Integration;

/// <summary>Settings → Health plans and folders, against a mocked ShareFile.</summary>
[Trait("Category", "Integration")]
public class CatalogAdminIntegrationTests : IAsyncLifetime
{
    private readonly FakeShareFile _sf = new();
    private readonly TestApp _app;
    private ApiClient _admin = null!;
    public CatalogAdminIntegrationTests() => _app = new TestApp(_sf);

    public async Task InitializeAsync()
    {
        await _app.AddUserAsync("admin@test.local", Roles.SuperAdmin);
        _admin = _app.NewClient(); await _admin.Login("admin@test.local");
    }
    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    private async Task<int> AddPlan(string name) => (await _admin.Post("/api/catalog/plans", new { name })).Json.GetProperty("planId").GetInt32();
    private async Task<List<System.Text.Json.JsonElement>> Plans() => (await _admin.Get("/api/catalog")).Json.EnumerateArray().ToList();

    private object Entry(int planId, string link, string dir = "Download", string? label = null, string? path = null, string? user = "svc-user") => new
    { planId, direction = dir, label, shareFileLink = link, shareFilePath = path, remoteKind = "Sftp", remoteHost = "sftp.example.com", remotePort = 22, remotePath = "/in", username = user };

    [Fact]
    public async Task Adding_a_plan_lists_it_and_duplicate_or_blank_names_are_refused()
    {
        await AddPlan("CAQH");
        Assert.Equal("CAQH", Assert.Single(await Plans()).GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.Post("/api/catalog/plans", new { name = "CAQH" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.Post("/api/catalog/plans", new { name = "   " })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.Post("/api/catalog/plans", new { name = new string('x', 101) })).Status);
    }

    [Fact]
    public async Task Plans_keep_their_creation_order_and_can_be_renamed()
    {
        var a = await AddPlan("Alpha"); await AddPlan("Beta");
        Assert.Equal(new[] { "Alpha", "Beta" }, (await Plans()).Select(p => p.GetProperty("name").GetString()));
        Assert.Equal(HttpStatusCode.OK, (await _admin.Patch($"/api/catalog/plans/{a}", new { name = "Alpha 2" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.Patch($"/api/catalog/plans/{a}", new { name = "Beta" })).Status);
        Assert.Equal("Alpha 2", (await Plans())[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_download_entry_gets_the_default_label_and_path()
    {
        var plan = await AddPlan("Aetna"); var f = _sf.AddFolder(name: "whatever");
        var r = await _admin.Post("/api/catalog/entries", Entry(plan, FakeShareFile.LinkFor(f.Id)));
        Assert.Equal(HttpStatusCode.OK, r.Status);
        var e = (await Plans())[0].GetProperty("entries")[0];
        Assert.Equal("Aetna", e.GetProperty("label").GetString());
        Assert.Equal("Shared Folders/SFTP Drop Aetna", e.GetProperty("shareFilePath").GetString());
        Assert.Equal(FakeShareFile.LinkFor(f.Id), e.GetProperty("shareFileLink").GetString());
        Assert.Equal("svc-user", e.GetProperty("username").GetString());
    }

    [Fact]
    public async Task An_upload_entry_defaults_to_the_SFTP_Uploads_path_and_explicit_values_win()
    {
        var plan = await AddPlan("Aetna"); var f1 = _sf.AddFolder(); var f2 = _sf.AddFolder();
        await _admin.Post("/api/catalog/entries", Entry(plan, FakeShareFile.LinkFor(f1.Id), "Upload"));
        await _admin.Post("/api/catalog/entries", Entry(plan, FakeShareFile.LinkFor(f2.Id), "Upload", "Stars", "Shared Folders/Elsewhere"));
        var entries = (await Plans())[0].GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal("Shared Folders/SFTP Uploads/Aetna", entries[0].GetProperty("shareFilePath").GetString());
        Assert.Equal("Stars", entries[1].GetProperty("label").GetString());
        Assert.Equal("Shared Folders/Elsewhere", entries[1].GetProperty("shareFilePath").GetString());
    }

    [Fact]
    public async Task Entries_must_carry_a_valid_sharefile_link_and_nothing_is_saved_otherwise()
    {
        var plan = await AddPlan("Aetna"); var folder = _sf.AddFolder(); var file = _sf.AddFile(folder.Id, "a.csv", id: "fo" + Guid.NewGuid().ToString("N")[..12]);
        string?[] bad = { null, "", "https://x.sharefile.com/home", FakeShareFile.LinkFor("fo0000000missing"), FakeShareFile.LinkFor(file.Id) };
        foreach (var link in bad)
            Assert.Equal(HttpStatusCode.BadRequest, (await _admin.Post("/api/catalog/entries", Entry(plan, link!))).Status);
        Assert.Empty((await Plans())[0].GetProperty("entries").EnumerateArray());
    }

    [Fact]
    public async Task A_disconnected_sharefile_blocks_saving_with_a_clear_message()
    {
        var plan = await AddPlan("Aetna"); var f = _sf.AddFolder(); _sf.Disconnected = true;
        var r = await _admin.Post("/api/catalog/entries", Entry(plan, FakeShareFile.LinkFor(f.Id)));
        Assert.Equal(HttpStatusCode.BadRequest, r.Status); Assert.Contains("Couldn't check", r.Error);
    }

    [Fact]
    public async Task Entries_for_unknown_plans_are_refused()
    {
        var f = _sf.AddFolder();
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.Post("/api/catalog/entries", Entry(9999, FakeShareFile.LinkFor(f.Id)))).Status);
    }

    [Fact]
    public async Task Editing_an_entry_updates_it_and_revalidates_the_link()
    {
        var plan = await AddPlan("Aetna"); var f1 = _sf.AddFolder(); var f2 = _sf.AddFolder();
        var id = (await _admin.Post("/api/catalog/entries", Entry(plan, FakeShareFile.LinkFor(f1.Id)))).Json.GetProperty("entryId").GetInt32();

        Assert.Equal(HttpStatusCode.OK, (await _admin.Put($"/api/catalog/entries/{id}", Entry(plan, FakeShareFile.LinkFor(f2.Id), label: "Renamed", user: "other"))).Status);
        var e = (await Plans())[0].GetProperty("entries")[0];
        Assert.Equal("Renamed", e.GetProperty("label").GetString()); Assert.Equal("other", e.GetProperty("username").GetString());
        Assert.Equal(FakeShareFile.LinkFor(f2.Id), e.GetProperty("shareFileLink").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.Put($"/api/catalog/entries/{id}", Entry(plan, "garbage"))).Status);
        Assert.Equal("Renamed", (await Plans())[0].GetProperty("entries")[0].GetProperty("label").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.Put("/api/catalog/entries/9999", Entry(plan, FakeShareFile.LinkFor(f2.Id)))).Status);
    }

    [Fact]
    public async Task Removing_an_entry_or_a_plan_hides_it_but_keeps_the_rows_for_the_audit_history()
    {
        var plan = await AddPlan("Aetna"); var f = _sf.AddFolder();
        var id = (await _admin.Post("/api/catalog/entries", Entry(plan, FakeShareFile.LinkFor(f.Id)))).Json.GetProperty("entryId").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await _admin.Delete($"/api/catalog/entries/{id}")).Status);
        Assert.Empty((await Plans())[0].GetProperty("entries").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.Delete($"/api/catalog/entries/{id}")).Status);

        await _admin.Post("/api/catalog/entries", Entry(plan, FakeShareFile.LinkFor(f.Id)));
        Assert.Equal(HttpStatusCode.OK, (await _admin.Delete($"/api/catalog/plans/{plan}")).Status);
        Assert.Empty(await Plans());
        Assert.Equal(2, _app.Query(db => db.CatalogEntries.Count(e => !e.IsActive)));   // soft delete
        Assert.Equal(1, _app.Query(db => db.HealthPlans.Count(p => !p.IsActive)));
    }

    [Fact]
    public async Task A_removed_plan_name_can_be_used_again()
    {
        var plan = await AddPlan("Aetna"); await _admin.Delete($"/api/catalog/plans/{plan}");
        Assert.Equal(HttpStatusCode.OK, (await _admin.Post("/api/catalog/plans", new { name = "Aetna" })).Status);
    }

    [Fact]
    public async Task Portal_entries_need_a_portal_name_and_do_not_keep_a_host()
    {
        var plan = await AddPlan("Aetna"); var f = _sf.AddFolder();
        var body = new { planId = plan, direction = "Download", shareFileLink = FakeShareFile.LinkFor(f.Id), remoteKind = "Portal", remoteHost = "x.example.com", remotePort = 22, portalName = "", remotePath = "/", username = "u" };
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.Post("/api/catalog/entries", body)).Status);
        var ok = new { planId = plan, direction = "Download", shareFileLink = FakeShareFile.LinkFor(f.Id), remoteKind = "Portal", remoteHost = "x.example.com", remotePort = 22, portalName = "Aetna SecureTransport", remotePath = "/", username = "u" };
        Assert.Equal(HttpStatusCode.OK, (await _admin.Post("/api/catalog/entries", ok)).Status);
        var e = (await Plans())[0].GetProperty("entries")[0];
        Assert.Equal("Portal", e.GetProperty("remoteKind").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, e.GetProperty("remoteHost").ValueKind);
    }

    [Fact]
    public async Task The_old_resolve_folders_endpoint_is_gone()
    {
        var r = await _admin.Post("/api/sharefile/resolve-folders");
        Assert.NotEqual(HttpStatusCode.OK, r.Status);
    }
}
