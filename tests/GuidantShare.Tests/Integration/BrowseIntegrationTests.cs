using System.Net;
using GuidantShare.Core;
using GuidantShare.Tests.Support;

namespace GuidantShare.Tests.Integration;

/// <summary>What a normal user sees: plans, folders, file listings and subfolders, with ShareFile mocked.</summary>
[Trait("Category", "Integration")]
public class BrowseIntegrationTests : IAsyncLifetime
{
    private readonly FakeShareFile _sf = new();
    private readonly TestApp _app;
    private ApiClient _user = null!;
    public BrowseIntegrationTests() => _app = new TestApp(_sf);

    public async Task InitializeAsync()
    {
        await _app.AddUserAsync("user@test.local", Roles.User);
        _user = _app.NewClient(); await _user.Login("user@test.local");
    }
    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    private static List<System.Text.Json.JsonElement> List(ApiResult r) => r.Json.EnumerateArray().ToList();

    [Fact]
    public async Task The_home_page_lists_plans_in_order_with_their_directions()
    {
        await _app.SeedEntryAsync("Anthem", Direction.Download, _sf.AddFolder().Id);
        await _app.SeedEntryAsync("Alignment", Direction.Download, _sf.AddFolder().Id);
        await _app.SeedEntryAsync("Alignment", Direction.Upload, _sf.AddFolder().Id);
        var plans = List(await _user.Get("/api/plans"));
        Assert.Equal(new[] { "Anthem", "Alignment" }, plans.Select(p => p.GetProperty("name").GetString()));
        Assert.False(plans[0].GetProperty("hasUpload").GetBoolean()); Assert.True(plans[0].GetProperty("hasDownload").GetBoolean());
        Assert.True(plans[1].GetProperty("hasUpload").GetBoolean());
    }

    [Fact]
    public async Task Two_accounts_pointing_at_one_sharefile_folder_show_once()
    {
        var shared = _sf.AddFolder().Id; var other = _sf.AddFolder().Id;
        await _app.SeedEntryAsync("Clever Care", Direction.Download, shared, "Main", username: "acct-1");
        await _app.SeedEntryAsync("Clever Care", Direction.Download, shared, "Main", username: "acct-2");
        await _app.SeedEntryAsync("Clever Care", Direction.Download, other, "Quality", username: "acct-1");
        var plan = _app.PlanId("Clever Care");
        var entries = List(await _user.Get($"/api/plans/{plan}/entries?direction=Download"));
        Assert.Equal(new[] { "Main", "Quality" }, entries.Select(e => e.GetProperty("label").GetString()));
    }

    [Fact]
    public async Task Entries_filter_by_direction_and_reject_a_bad_one()
    {
        await _app.SeedEntryAsync("Aetna", Direction.Download, _sf.AddFolder().Id);
        var plan = _app.PlanId("Aetna");
        Assert.Empty(List(await _user.Get($"/api/plans/{plan}/entries?direction=Upload")));
        Assert.Equal(HttpStatusCode.BadRequest, (await _user.Get($"/api/plans/{plan}/entries?direction=Sideways")).Status);
    }

    [Fact]
    public async Task Files_are_listed_newest_first_with_their_sizes()
    {
        var root = _sf.AddFolder().Id;
        _sf.AddFile(root, "old.csv", 5, DateTime.UtcNow.AddDays(-3)); _sf.AddFile(root, "new.csv", 2048, DateTime.UtcNow);
        var entry = await _app.SeedEntryAsync("Aetna", Direction.Download, root);
        var r = await _user.Get($"/api/entries/{entry}/files?refresh=true");
        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.Equal(new[] { "new.csv", "old.csv" }, r.Json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()));
        Assert.Equal(2, r.Json.GetProperty("total").GetInt32()); Assert.True(r.Json.GetProperty("isRoot").GetBoolean());
        Assert.Equal(FakeShareFile.LinkFor(root), r.Json.GetProperty("folderLink").GetString());
    }

    [Fact]
    public async Task Subfolders_are_listed_and_can_be_opened_even_when_the_entry_is_a_parent_folder()
    {
        // Regression: Clever Care "FromCCHP" points at the parent folder and must show everything beneath it.
        var root = _sf.AddFolder(name: "IPA ACCESS").Id;
        var quality = _sf.AddFolder(name: "Quality", parentId: root); _sf.AddFile(quality.Id, "q1.pdf", 100);
        var deep = _sf.AddFolder(name: "2026", parentId: quality.Id); _sf.AddFile(deep.Id, "q-2026.pdf", 50);
        _sf.AddFile(root, "readme.txt");
        var entry = await _app.SeedEntryAsync("Clever Care", Direction.Download, root, "FromCCHP");

        var top = await _user.Get($"/api/entries/{entry}/files?refresh=true");
        Assert.Equal("Quality", Assert.Single(top.Json.GetProperty("folders").EnumerateArray()).GetProperty("name").GetString());

        var inQuality = await _user.Get($"/api/entries/{entry}/files?folder={quality.Id}&refresh=true");
        Assert.Equal(HttpStatusCode.OK, inQuality.Status); Assert.False(inQuality.Json.GetProperty("isRoot").GetBoolean());
        Assert.Equal("q1.pdf", Assert.Single(inQuality.Json.GetProperty("items").EnumerateArray()).GetProperty("name").GetString());
        Assert.EndsWith("/Quality", inQuality.Json.GetProperty("location").GetProperty("shareFilePath").GetString());

        var inDeep = await _user.Get($"/api/entries/{entry}/files?folder={deep.Id}&refresh=true");   // two levels down
        Assert.Equal(HttpStatusCode.OK, inDeep.Status);
        Assert.EndsWith("/Quality/2026", inDeep.Json.GetProperty("location").GetProperty("shareFilePath").GetString());
    }

    [Fact]
    public async Task A_folder_outside_the_entry_cannot_be_opened()
    {
        var root = _sf.AddFolder().Id; var elsewhere = _sf.AddFolder(name: "Secret").Id; _sf.AddFile(elsewhere, "secret.csv");
        var entry = await _app.SeedEntryAsync("Aetna", Direction.Download, root);
        var r = await _user.Get($"/api/entries/{entry}/files?folder={elsewhere}&refresh=true");
        Assert.Equal(HttpStatusCode.BadRequest, r.Status);
    }

    [Fact]
    public async Task Search_filters_files_and_subfolders_and_paging_limits_the_page()
    {
        var root = _sf.AddFolder().Id; _sf.AddFolder(name: "Reports", parentId: root); _sf.AddFolder(name: "Other", parentId: root);
        for (var i = 1; i <= 5; i++) _sf.AddFile(root, $"claims-{i}.csv", i, DateTime.UtcNow.AddMinutes(-i));
        _sf.AddFile(root, "eligibility.csv");
        var entry = await _app.SeedEntryAsync("Aetna", Direction.Download, root);

        var s = await _user.Get($"/api/entries/{entry}/files?search=claims&refresh=true");
        Assert.Equal(5, s.Json.GetProperty("total").GetInt32()); Assert.Empty(s.Json.GetProperty("folders").EnumerateArray());
        var folders = await _user.Get($"/api/entries/{entry}/files?search=report&refresh=true");
        Assert.Equal("Reports", Assert.Single(folders.Json.GetProperty("folders").EnumerateArray()).GetProperty("name").GetString());

        var page2 = await _user.Get($"/api/entries/{entry}/files?pageSize=4&page=2&refresh=true");
        Assert.Equal(6, page2.Json.GetProperty("total").GetInt32()); Assert.Equal(2, page2.Json.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Removed_plans_and_entries_disappear_for_users()
    {
        var entry = await _app.SeedEntryAsync("Aetna", Direction.Download, _sf.AddFolder().Id);
        _app.Query(db => { foreach (var p in db.HealthPlans) { p.IsActive = false; foreach (var e in db.CatalogEntries) e.IsActive = false; } return db.SaveChanges(); });
        Assert.Empty(List(await _user.Get("/api/plans")));
        Assert.Equal(HttpStatusCode.NotFound, (await _user.Get($"/api/entries/{entry}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await _user.Get($"/api/entries/{entry}/files")).Status);
    }

    [Fact]
    public async Task A_disconnected_sharefile_gives_a_503_the_ui_can_explain()
    {
        var entry = await _app.SeedEntryAsync("Aetna", Direction.Download, _sf.AddFolder().Id);
        _sf.Disconnected = true;
        var r = await _user.Get($"/api/entries/{entry}/files?refresh=true");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.Status); Assert.Equal("disconnected", r.Json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_sharefile_outage_is_reported_as_a_502()
    {
        var entry = await _app.SeedEntryAsync("Aetna", Direction.Download, _sf.AddFolder().Id);
        _sf.ListError = new HttpRequestException("ShareFile returned 500.");
        var r = await _user.Get($"/api/entries/{entry}/files?refresh=true");
        Assert.Equal(HttpStatusCode.BadGateway, r.Status); Assert.Contains("could not be reached", r.Error);
    }
}
