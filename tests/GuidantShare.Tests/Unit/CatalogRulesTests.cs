using GuidantShare.Api;
using GuidantShare.Core;
using GuidantShare.Tests.Support;

namespace GuidantShare.Tests.Unit;

/// <summary>Pure logic, no host: link parsing, entry validation and the label / path defaults.</summary>
[Trait("Category", "Unit")]
public class CatalogRulesTests
{
    private const string Id = "fo0ea564-6572-49c0-9f79-416c34cea03c";

    [Theory]
    [InlineData("https://guidantmso.sharefile.com/home/shared/" + Id, Id)]
    [InlineData("  https://guidantmso.sharefile.com/home/shared/" + Id + "  ", Id)]
    [InlineData(Id, Id)]
    [InlineData("https://guidantmso.sharefile.com/home/shared/" + Id + "?x=1", Id)]
    [InlineData("https://guidantmso.sharefile.com/home", null)]
    [InlineData("not a link", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ParseFolderId_finds_the_fo_id(string? link, string? expected) => Assert.Equal(expected, EndpointsExt.ParseFolderId(link));

    private static EntryReq Req(string? link = null, string? dir = "Download", string? label = null, string? path = null, string? kind = "Sftp",
        string? host = "sftp.example.com", int? port = 22, string? portal = null, string? username = "svc", string? remotePath = "/in") =>
        new(1, dir, label, link, path, kind, host, port, remotePath, portal, null, username);

    private static (FakeShareFile sf, string link) WithFolder(string name = "SFTP Drop Aetna")
    {
        var sf = new FakeShareFile(); var f = sf.AddFolder(name: name);
        return (sf, FakeShareFile.LinkFor(f.Id));
    }

    private static Task<string?> Apply(CatalogEntry e, string plan, EntryReq r, IShareFileService sf) => EndpointsExt.ApplyEntryAsync(e, plan, r, sf, default);

    [Fact]
    public async Task Blank_label_defaults_to_the_plan_name()
    {
        var (sf, link) = WithFolder(); var e = new CatalogEntry();
        Assert.Null(await Apply(e, "Aetna", Req(link), sf));
        Assert.Equal("Aetna", e.Label);
    }

    [Fact]
    public async Task Download_display_path_defaults_to_SFTP_Drop_plan()
    {
        var (sf, link) = WithFolder(); var e = new CatalogEntry();
        Assert.Null(await Apply(e, "Aetna", Req(link, "Download"), sf));
        Assert.Equal("Shared Folders/SFTP Drop Aetna", e.SharefileDisplayPath);
    }

    [Fact]
    public async Task Upload_display_path_defaults_to_SFTP_Uploads_plan()
    {
        var (sf, link) = WithFolder(); var e = new CatalogEntry();
        Assert.Null(await Apply(e, "Aetna", Req(link, "Upload"), sf));
        Assert.Equal("Shared Folders/SFTP Uploads/Aetna", e.SharefileDisplayPath);
        Assert.Equal(Direction.Upload, e.Direction);
    }

    [Fact]
    public async Task Explicit_label_and_path_are_kept_and_the_folder_id_comes_from_the_link()
    {
        var (sf, link) = WithFolder(); var e = new CatalogEntry();
        Assert.Null(await Apply(e, "Aetna", Req(link, label: "Quality", path: "Shared Folders/Custom/Path"), sf));
        Assert.Equal("Quality", e.Label); Assert.Equal("Shared Folders/Custom/Path", e.SharefileDisplayPath);
        Assert.Equal(EndpointsExt.ParseFolderId(link), e.SharefileFolderId);
    }

    [Fact]
    public async Task Sftp_fields_are_saved_and_port_defaults_to_22()
    {
        var (sf, link) = WithFolder(); var e = new CatalogEntry();
        Assert.Null(await Apply(e, "Aetna", Req(link, port: null, username: "  svc-a  "), sf));
        Assert.Equal(22, e.RemotePort); Assert.Equal("sftp.example.com", e.RemoteHost); Assert.Equal("svc-a", e.RemoteUsername);
        Assert.Equal(RemoteKind.Sftp, e.RemoteKind);
    }

    [Fact]
    public async Task Portal_entries_drop_host_and_port()
    {
        var (sf, link) = WithFolder(); var e = new CatalogEntry();
        Assert.Null(await Apply(e, "Aetna", Req(link, kind: "Portal", host: "ignored.example.com", port: 22, portal: "Aetna SecureTransport"), sf));
        Assert.Null(e.RemoteHost); Assert.Null(e.RemotePort); Assert.Equal("Aetna SecureTransport", e.PortalName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task The_sharefile_link_is_required(string? link)
    {
        var (sf, _) = WithFolder();
        Assert.Contains("required", await Apply(new CatalogEntry(), "Aetna", Req(link), sf));
    }

    [Fact]
    public async Task A_link_without_a_folder_id_is_rejected_without_calling_sharefile()
    {
        var (sf, _) = WithFolder();
        Assert.Contains("doesn't look like", await Apply(new CatalogEntry(), "Aetna", Req("https://x.sharefile.com/home"), sf));
        Assert.Empty(sf.Lookups);
    }

    [Fact]
    public async Task A_folder_sharefile_cannot_find_is_rejected()
    {
        var (sf, _) = WithFolder();
        Assert.Contains("can't find", await Apply(new CatalogEntry(), "Aetna", Req(FakeShareFile.LinkFor("fo0000000000missing")), sf));
    }

    [Fact]
    public async Task A_link_to_a_file_is_rejected()
    {
        var (sf, _) = WithFolder(); var folder = sf.AddFolder(name: "x"); var file = sf.AddFile(folder.Id, "a.csv", id: "fo" + Guid.NewGuid().ToString("N")[..12]);   // a file whose id also looks like a link target
        Assert.Contains("file, not a folder", await Apply(new CatalogEntry(), "Aetna", Req(FakeShareFile.LinkFor(file.Id)), sf));
    }

    [Fact]
    public async Task A_disconnected_sharefile_gives_a_readable_error()
    {
        var (sf, link) = WithFolder(); sf.Disconnected = true;
        Assert.Contains("Couldn't check", await Apply(new CatalogEntry(), "Aetna", Req(link), sf));
    }

    [Fact]
    public async Task A_rejected_entry_is_left_untouched()
    {
        var (sf, _) = WithFolder(); var e = new CatalogEntry { Label = "Old", SharefileFolderId = "foold000000" };
        await Apply(e, "Aetna", Req("bad link", label: "New"), sf);
        Assert.Equal("Old", e.Label); Assert.Equal("foold000000", e.SharefileFolderId);
    }

    [Theory]
    [InlineData("Sideways", "Sftp", "sftp.example.com", null, "Choose Download or Upload")]
    [InlineData("Download", "Carrier pigeon", "sftp.example.com", null, "Choose SFTP or Portal")]
    [InlineData("Download", "Sftp", "", null, "Enter the SFTP host")]
    [InlineData("Download", "Portal", null, "", "Enter the portal name")]
    public async Task Required_fields_are_checked(string dir, string kind, string? host, string? portal, string expected)
    {
        var (sf, link) = WithFolder();
        Assert.Contains(expected, await Apply(new CatalogEntry(), "Aetna", Req(link, dir, kind: kind, host: host, portal: portal), sf));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public async Task Ports_outside_1_to_65535_are_rejected(int port)
    {
        var (sf, link) = WithFolder();
        Assert.Contains("port", await Apply(new CatalogEntry(), "Aetna", Req(link, port: port)), StringComparison.OrdinalIgnoreCase);
        async Task<string?> Apply(CatalogEntry e, string p, EntryReq r) => await EndpointsExt.ApplyEntryAsync(e, p, r, sf, default);
    }

    [Fact]
    public async Task Overlong_label_and_username_are_rejected()
    {
        var (sf, link) = WithFolder();
        Assert.Contains("too long", await Apply(new CatalogEntry(), "Aetna", Req(link, label: new string('x', 101)), sf));
        Assert.Contains("too long", await Apply(new CatalogEntry(), "Aetna", Req(link, username: new string('u', 201)), sf));
    }

    [Fact]
    public void RemoteDisplay_describes_sftp_and_portal_entries()
    {
        Assert.Equal("sftp.example.com:22 /in", new CatalogEntry { RemoteKind = RemoteKind.Sftp, RemoteHost = "sftp.example.com", RemotePort = 22, RemotePath = "/in" }.RemoteDisplay);
        Assert.Equal("Aetna SecureTransport /out", new CatalogEntry { RemoteKind = RemoteKind.Portal, PortalName = "Aetna SecureTransport", RemotePath = "/out" }.RemoteDisplay);
        Assert.Equal("Champion Portal", new CatalogEntry { RemoteKind = RemoteKind.Portal, PortalName = "Champion Portal" }.RemoteDisplay);
    }
}
