using System.Net;
using GuidantShare.Core;
using GuidantShare.Tests.Support;
using GuidantShare.ShareFile;
using Microsoft.Extensions.Logging.Abstractions;

namespace GuidantShare.Tests.Unit;

/// <summary>ShareFileService on its own, against the fake HTTP API and an in-memory secret store.</summary>
[Trait("Category", "Unit")]
public class ShareFileServiceTests
{
    private class MemSecrets : ISecretStore
    {
        public Dictionary<string, string> D = new();
        public Task<string?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult(D.TryGetValue(key, out var v) ? v : null);
        public Task SetAsync(string key, string value, CancellationToken ct = default) { D[key] = value; return Task.CompletedTask; }
        public Task DeleteAsync(string key, CancellationToken ct = default) { D.Remove(key); return Task.CompletedTask; }
    }

    private static (ShareFileService sf, FakeShareFileHttp http, MemSecrets secrets) Make()
    {
        var http = new FakeShareFileHttp(); var secrets = new MemSecrets();
        return (new ShareFileService(secrets, new HttpClient(http), NullLogger<ShareFileService>.Instance), http, secrets);
    }

    [Fact]
    public async Task Status_follows_configure_then_connect_then_disconnect()
    {
        var (sf, _, _) = Make();
        Assert.False((await sf.GetStatusAsync()).Configured);
        await sf.SaveAppSettingsAsync("Test", "cid", "secret", null);
        var st = await sf.GetStatusAsync();
        Assert.True(st.Configured); Assert.False(st.Connected); Assert.Equal("test", st.Subdomain);   // subdomain is lower-cased
        await sf.ConnectWithCodeAsync("good-code", "admin@x.com");
        Assert.True((await sf.GetStatusAsync()).Connected);
        await sf.DisconnectAsync();
        Assert.False((await sf.GetStatusAsync()).Connected);
    }

    [Fact]
    public async Task A_bad_code_is_refused_and_leaves_it_disconnected()
    {
        var (sf, _, _) = Make(); await sf.SaveAppSettingsAsync("test", "cid", "secret", null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sf.ConnectWithCodeAsync("wrong", "admin@x.com"));
        Assert.False((await sf.GetStatusAsync()).Connected);
    }

    [Fact]
    public async Task Calls_before_connecting_say_so()
    {
        var (sf, _, _) = Make(); await sf.SaveAppSettingsAsync("test", "cid", "secret", null);
        await Assert.ThrowsAsync<ShareFileNotConnectedException>(() => sf.GetItemAsync("fo1234567890"));
    }

    [Fact]
    public async Task The_client_secret_is_kept_when_settings_are_saved_again_without_one()
    {
        var (sf, _, secrets) = Make();
        await sf.SaveAppSettingsAsync("test", "cid", "secret-1", null);
        await sf.SaveAppSettingsAsync("test", "cid", "", null);
        Assert.Equal("secret-1", secrets.D["sharefile.client_secret"]);
    }

    [Fact]
    public async Task Items_children_and_missing_folders()
    {
        var (sf, http, _) = Make(); await sf.SaveAppSettingsAsync("test", "cid", "secret", null); await sf.ConnectWithCodeAsync("good-code", "a@b.c");
        http.AddFolder("foroot000001", "Root"); http.AddFolder("fochild00001", "Sub", "foroot000001"); http.AddFile("foroot000001", "a.csv", new byte[] { 1, 2, 3 });

        var item = await sf.GetItemAsync("foroot000001");
        Assert.True(item!.IsFolder); Assert.Equal("Root", item.Name);
        var kids = await sf.ListChildrenAsync("foroot000001");
        Assert.Equal(2, kids.Count); Assert.Contains(kids, k => k.IsFolder && k.Name == "Sub"); Assert.Contains(kids, k => !k.IsFolder && k.Size == 3);
        Assert.Null(await sf.GetItemAsync("fonothere0001"));
    }

    [Fact]
    public async Task Upload_negotiates_then_posts_the_raw_bytes()
    {
        var (sf, http, _) = Make(); await sf.SaveAppSettingsAsync("test", "cid", "secret", null); await sf.ConnectWithCodeAsync("good-code", "a@b.c");
        http.AddFolder("foup00000001", "Uploads");
        var data = new byte[] { 9, 8, 7, 6 };
        var item = await sf.UploadAsync("foup00000001", "report 1.csv", new MemoryStream(data), data.Length, overwrite: false);
        Assert.Equal("report 1.csv", item.Name); Assert.Equal(data, http.LastUploadBytes);
        Assert.Contains(http.Requests, r => r == "POST /sf/v3/Items(foup00000001)/Upload");
        Assert.Contains(http.Requests, r => r.StartsWith("POST /chunk/"));
    }

    [Fact]
    public async Task Web_link_opens_the_folder_in_the_sharefile_web_app()
    {
        var (sf, _, _) = Make(); await sf.SaveAppSettingsAsync("Acme", "cid", "secret", null);
        Assert.Equal("https://acme.sharefile.com/home/shared/fo1234567890", await sf.GetWebLinkAsync("fo1234567890"));
    }
}
