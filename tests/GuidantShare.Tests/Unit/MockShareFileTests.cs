using System.Text;
using GuidantShare.Core;
using GuidantShare.ShareFile;

namespace GuidantShare.Tests.Unit;

/// <summary>The offline ShareFile used for local development (ShareFile:Mode=Mock).</summary>
[Trait("Category", "Unit")]
public class MockShareFileTests
{
    [Fact]
    public async Task Any_fo_folder_exists_with_sample_files_and_a_subfolder()
    {
        var sf = new MockShareFileService();
        Assert.NotNull(await sf.GetItemAsync("fo-dev-anything"));
        Assert.Null(await sf.GetItemAsync("fi-not-a-folder"));

        var items = await sf.ListChildrenAsync("fo-dev-anything");
        Assert.Contains(items, i => i.IsFolder && i.Name == "Archive");
        Assert.Equal(2, items.Count(i => !i.IsFolder));
        var archive = items.Single(i => i.IsFolder);
        Assert.Single(await sf.ListChildrenAsync(archive.Id));
    }

    [Fact]
    public async Task Uploads_appear_in_the_listing_and_overwrite_replaces_by_name()
    {
        var sf = new MockShareFileService();
        await sf.UploadAsync("fo1", "a.csv", new MemoryStream(Encoding.UTF8.GetBytes("12345")), 5, false);
        await sf.UploadAsync("fo1", "A.CSV", new MemoryStream(Encoding.UTF8.GetBytes("123")), 3, true);
        var files = (await sf.ListChildrenAsync("fo1")).Where(i => i.Name.Equals("a.csv", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(3, Assert.Single(files).Size);
    }

    [Fact]
    public async Task Disconnecting_makes_calls_fail_like_the_real_service_and_connecting_restores_them()
    {
        var sf = new MockShareFileService();
        await sf.DisconnectAsync();
        Assert.False((await sf.GetStatusAsync()).Connected);
        await Assert.ThrowsAsync<ShareFileNotConnectedException>(() => sf.ListChildrenAsync("fo1"));
        await sf.ConnectWithCodeAsync("any", "tester");
        Assert.True((await sf.GetStatusAsync()).Connected);
        Assert.NotEmpty(await sf.ListChildrenAsync("fo1"));
    }

    [Fact]
    public async Task Web_links_point_at_the_folder_id()
    {
        Assert.EndsWith("/fo123", await new MockShareFileService().GetWebLinkAsync("fo123"));
    }
}
