using System.Net;
using GuidantShare.Core;
using GuidantShare.Tests.Support;

namespace GuidantShare.Tests.Integration;

/// <summary>What a normal user sees: plans and folders (from FileSync), file listings and subfolders, with ShareFile and FileSync mocked.</summary>
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

    private static List<System.Text.Json.JsonElement> Plans(ApiResult r) => r.Json.GetProperty("plans").EnumerateArray().ToList();

    [Fact]
    public async Task The_home_page_lists_the_plans_FileSync_provides_with_their_directions()
    {
        _app.Seed("Anthem", Direction.Download, _sf.AddFolder().Id);
        _app.Seed("Alignment", Direction.Download, _sf.AddFolder().Id);
        _app.Seed("Alignment", Direction.Upload, _sf.AddFolder().Id);
        var plans = Plans(await _user.Get("/api/plans"));
        Assert.Equal(new[] { "Anthem", "Alignment" }, plans.Select(p => p.GetProperty("name").GetString()));
        Assert.Equal("anthem", plans[0].GetProperty("key").GetString());
        Assert.False(plans[0].GetProperty("hasUpload").GetBoolean()); Assert.True(plans[0].GetProperty("hasDownload").GetBoolean());
        Assert.True(plans[1].GetProperty("hasUpload").GetBoolean());
    }

    [Fact]
    public async Task A_plan_lists_its_folders_once_each_even_when_several_FileSync_jobs_feed_them()
    {
        // FileSync already merged "Clever Care 1" and "Clever Care 2" into one 278 and one UM; the site just shows what it is given
        _app.Seed("Clever Care", Direction.Upload, _sf.AddFolder().Id, "278");
        _app.Seed("Clever Care", Direction.Upload, _sf.AddFolder().Id, "UM");
        var r = await _user.Get("/api/plans/clever-care");
        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.Equal("Clever Care", r.Json.GetProperty("name").GetString());
        Assert.Equal(new[] { "278", "UM" }, r.Json.GetProperty("folders").EnumerateArray().Select(f => f.GetProperty("label").GetString()));
        Assert.All(r.Json.GetProperty("folders").EnumerateArray(), f => Assert.Equal("Upload", f.GetProperty("direction").GetString()));
    }

    [Fact]
    public async Task An_unknown_plan_or_folder_is_a_404()
    {
        var url = _app.Seed("Aetna", Direction.Download, _sf.AddFolder().Id);
        Assert.Equal(HttpStatusCode.NotFound, (await _user.Get("/api/plans/nope")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await _user.Get("/api/plans/aetna/folders/nope/files")).Status);
        Assert.Equal(HttpStatusCode.OK, (await _user.Get($"{url}/files")).Status);
    }

    [Fact]
    public async Task Files_are_listed_newest_first_with_their_sizes()
    {
        var root = _sf.AddFolder().Id;
        _sf.AddFile(root, "old.csv", 5, DateTime.UtcNow.AddDays(-3)); _sf.AddFile(root, "new.csv", 2048, DateTime.UtcNow);
        var url = _app.Seed("Aetna", Direction.Download, root);
        var r = await _user.Get($"{url}/files?refresh=true");
        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.Equal(new[] { "new.csv", "old.csv" }, r.Json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()));
        Assert.Equal(2, r.Json.GetProperty("total").GetInt32()); Assert.True(r.Json.GetProperty("isRoot").GetBoolean());
        Assert.Equal(FakeShareFile.LinkFor(root), r.Json.GetProperty("folderLink").GetString());
        Assert.Equal("Download", r.Json.GetProperty("direction").GetString());
    }

    [Fact]
    public async Task Subfolders_are_listed_and_can_be_opened_even_when_the_folder_is_a_parent()
    {
        var root = _sf.AddFolder(name: "IPA ACCESS").Id;
        var quality = _sf.AddFolder(name: "Quality", parentId: root); _sf.AddFile(quality.Id, "q1.pdf", 100);
        var deep = _sf.AddFolder(name: "2026", parentId: quality.Id); _sf.AddFile(deep.Id, "q-2026.pdf", 50);
        _sf.AddFile(root, "readme.txt");
        var url = _app.Seed("Clever Care", Direction.Download, root, "FromCCHP");

        var top = await _user.Get($"{url}/files?refresh=true");
        Assert.Equal("Quality", Assert.Single(top.Json.GetProperty("folders").EnumerateArray()).GetProperty("name").GetString());

        var inQuality = await _user.Get($"{url}/files?folder={quality.Id}&refresh=true");
        Assert.Equal(HttpStatusCode.OK, inQuality.Status); Assert.False(inQuality.Json.GetProperty("isRoot").GetBoolean());
        Assert.Equal("q1.pdf", Assert.Single(inQuality.Json.GetProperty("items").EnumerateArray()).GetProperty("name").GetString());
        Assert.EndsWith("/Quality", inQuality.Json.GetProperty("location").GetProperty("shareFilePath").GetString());

        var inDeep = await _user.Get($"{url}/files?folder={deep.Id}&refresh=true");
        Assert.Equal(HttpStatusCode.OK, inDeep.Status);
        Assert.EndsWith("/Quality/2026", inDeep.Json.GetProperty("location").GetProperty("shareFilePath").GetString());
    }

    [Fact]
    public async Task A_folder_outside_the_plan_folder_cannot_be_opened()
    {
        var root = _sf.AddFolder().Id; var elsewhere = _sf.AddFolder(name: "Secret").Id; _sf.AddFile(elsewhere, "secret.csv");
        var url = _app.Seed("Aetna", Direction.Download, root);
        Assert.Equal(HttpStatusCode.BadRequest, (await _user.Get($"{url}/files?folder={elsewhere}&refresh=true")).Status);
    }

    [Fact]
    public async Task A_subfolder_seen_through_one_folder_cannot_be_opened_through_another()
    {
        var rootA = _sf.AddFolder().Id; var sub = _sf.AddFolder(name: "Inside A", parentId: rootA).Id;
        var urlA = _app.Seed("Plan A", Direction.Download, rootA);
        var urlB = _app.Seed("Plan B", Direction.Download, _sf.AddFolder().Id);
        await _user.Get($"{urlA}/files?refresh=true");
        Assert.Equal(HttpStatusCode.OK, (await _user.Get($"{urlA}/files?folder={sub}&refresh=true")).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await _user.Get($"{urlB}/files?folder={sub}&refresh=true")).Status);
    }

    [Fact]
    public async Task Search_filters_files_and_subfolders_and_paging_limits_the_page()
    {
        var root = _sf.AddFolder().Id; _sf.AddFolder(name: "Reports", parentId: root); _sf.AddFolder(name: "Other", parentId: root);
        for (var i = 1; i <= 5; i++) _sf.AddFile(root, $"claims-{i}.csv", i, DateTime.UtcNow.AddMinutes(-i));
        _sf.AddFile(root, "eligibility.csv");
        var url = _app.Seed("Aetna", Direction.Download, root);

        var s = await _user.Get($"{url}/files?search=claims&refresh=true");
        Assert.Equal(5, s.Json.GetProperty("total").GetInt32()); Assert.Empty(s.Json.GetProperty("folders").EnumerateArray());
        var folders = await _user.Get($"{url}/files?search=report&refresh=true");
        Assert.Equal("Reports", Assert.Single(folders.Json.GetProperty("folders").EnumerateArray()).GetProperty("name").GetString());

        var page2 = await _user.Get($"{url}/files?pageSize=4&page=2&refresh=true");
        Assert.Equal(6, page2.Json.GetProperty("total").GetInt32()); Assert.Equal(2, page2.Json.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task A_plan_removed_in_FileSync_disappears_for_users()
    {
        var url = _app.Seed("Aetna", Direction.Download, _sf.AddFolder().Id);
        Assert.Single(Plans(await _user.Get("/api/plans?refresh=true")));
        _app.FileSync.Plans.Clear();   // the job was disabled or deleted in FileSync
        Assert.Empty(Plans(await _user.Get("/api/plans?refresh=true")));
        Assert.Equal(HttpStatusCode.NotFound, (await _user.Get("/api/plans/aetna")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await _user.Get($"{url}/files")).Status);
    }

    [Fact]
    public async Task A_disconnected_sharefile_gives_a_503_the_ui_can_explain()
    {
        var url = _app.Seed("Aetna", Direction.Download, _sf.AddFolder().Id);
        _sf.Disconnected = true;
        var r = await _user.Get($"{url}/files?refresh=true");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.Status); Assert.Equal("disconnected", r.Json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_sharefile_outage_is_reported_as_a_502()
    {
        var url = _app.Seed("Aetna", Direction.Download, _sf.AddFolder().Id);
        _sf.ListError = new HttpRequestException("ShareFile returned 500.");
        var r = await _user.Get($"{url}/files?refresh=true");
        Assert.Equal(HttpStatusCode.BadGateway, r.Status); Assert.Contains("could not be reached", r.Error);
    }

    [Fact]
    public async Task When_FileSync_goes_down_the_last_catalog_is_still_served_and_marked_stale()
    {
        var root = _sf.AddFolder().Id; _sf.AddFile(root, "a.csv");
        var url = _app.Seed("Aetna", Direction.Download, root);
        Assert.False((await _user.Get("/api/plans")).Json.GetProperty("stale").GetBoolean());

        _app.FileSync.Down = true;
        var plans = await _user.Get("/api/plans");
        Assert.Equal(HttpStatusCode.OK, plans.Status); Assert.True(plans.Json.GetProperty("stale").GetBoolean());
        var files = await _user.Get($"{url}/files?refresh=true");   // ShareFile browsing keeps working
        Assert.Equal(HttpStatusCode.OK, files.Status); Assert.True(files.Json.GetProperty("stale").GetBoolean());
    }

    [Fact]
    public async Task When_FileSync_is_down_and_nothing_was_ever_loaded_users_get_a_clear_503()
    {
        // Its own app: FileSync must be down before the app starts, because startup already reads the catalog once
        using var app = new TestApp(new FakeShareFile(), new FakeFileSync { Down = true });
        await app.AddUserAsync("late@test.local", Roles.User);
        var user = app.NewClient(); await user.Login("late@test.local");
        var r = await user.Get("/api/plans");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.Status); Assert.Equal("filesync_unavailable", r.Json.GetProperty("code").GetString());
    }
}
