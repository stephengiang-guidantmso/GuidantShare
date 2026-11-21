using System.Net;
using System.Text;
using GuidantShare.Api.Services;
using GuidantShare.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace GuidantShare.Tests.Unit;

[Trait("Category", "Unit")]
public class FileSyncProxyRulesTests
{
    [Theory]
    [InlineData("GET", "workflows")]
    [InlineData("POST", "workflows")]
    [InlineData("PUT", "workflows/wf-aetnadownload-3956")]
    [InlineData("DELETE", "workflows/wf-aetnadownload-3956")]
    [InlineData("POST", "workflows/wf-1/run")]
    [InlineData("POST", "workflows/wf-1/toggle")]
    [InlineData("POST", "workflows/wf-1/cancel")]
    [InlineData("POST", "workflows/wf-1/verify")]
    [InlineData("GET", "workflows/wf-1/browse")]
    [InlineData("GET", "workflows/wf-1/verifications/latest")]
    [InlineData("GET", "verifications/3f2a/csv")]
    [InlineData("POST", "sftp/verify")]
    [InlineData("GET", "transfers")]
    [InlineData("GET", "transfers/count")]
    [InlineData("POST", "transfers/retry-all")]
    [InlineData("POST", "transfers/abc-123/retry")]
    [InlineData("GET", "transfers/abc-123/logs")]
    [InlineData("GET", "logs")]
    [InlineData("GET", "catalog")]
    [InlineData("GET", "portal-code")]
    [InlineData("POST", "portal-code")]
    [InlineData("DELETE", "portal-code")]
    public void The_screens_calls_are_allowed(string method, string path)
    {
        Assert.True(FileSyncProxyRules.IsAllowed(method, path, out var target));
        Assert.Equal("api/" + path, target);
    }

    [Theory]
    [InlineData("GET", "workflows/wf-1/credentials")]
    [InlineData("POST", "workflows/wf-1/credentials")]
    [InlineData("GET", "debug/state")]
    [InlineData("GET", "staging")]
    [InlineData("GET", "swagger")]
    [InlineData("GET", "swagger/v1/swagger.json")]
    [InlineData("GET", "workflows/../debug/state")]
    [InlineData("GET", "workflows/%2e%2e/credentials")]
    [InlineData("GET", "workflows\\wf-1")]
    [InlineData("GET", "workflows//wf-1")]
    [InlineData("GET", "/workflows")]
    [InlineData("GET", "workflows/")]
    [InlineData("GET", "")]
    [InlineData("GET", null)]
    [InlineData("PATCH", "workflows/wf-1")]
    [InlineData("DELETE", "workflows")]
    [InlineData("PUT", "transfers")]
    [InlineData("GET", "workflows/wf-1/run")]
    [InlineData("POST", "workflows/wf 1/run")]
    public void Everything_else_is_refused(string method, string? path)
    {
        Assert.False(FileSyncProxyRules.IsAllowed(method, path, out var target));
        Assert.Equal("", target);
    }

    [Theory]
    [InlineData("GET", "workflows", false)]
    [InlineData("POST", "workflows", true)]
    [InlineData("DELETE", "workflows/wf-1", true)]
    [InlineData("POST", "portal-code", false)]
    [InlineData("DELETE", "portal-code", true)]
    public void Only_changes_are_audited_and_never_the_portal_code(string method, string path, bool audited) =>
        Assert.Equal(audited, FileSyncProxyRules.ShouldAudit(method, path));
}

[Trait("Category", "Unit")]
public class FileSyncClientTests
{
    private class Handler : HttpMessageHandler
    {
        public int Calls; public bool Fail; public string? ApiKey; public string Body = "{\"plans\":[{\"key\":\"aetna\",\"name\":\"Aetna\",\"folders\":[{\"key\":\"download-aetna\",\"label\":\"Aetna\",\"direction\":\"Download\",\"shareFileFolderId\":\"fo1\",\"shareFileDisplayPath\":\"Shared Folders/Aetna\",\"workflowIds\":[\"wf-1\"]}]}],\"issues\":[]}";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            ApiKey = request.Headers.TryGetValues("X-Api-Key", out var v) ? v.FirstOrDefault() : null;
            if (Fail) throw new HttpRequestException("connection refused");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body, Encoding.UTF8, "application/json") });
        }
    }

    private static (FileSyncClient client, Handler handler) Make(int cacheSeconds = 45)
    {
        var h = new Handler();
        return (new FileSyncClient(new HttpClient(h), new FileSyncOptions { BaseUrl = "http://localhost:5000", ApiKey = "test-key", CacheSeconds = cacheSeconds },
            NullLogger<FileSyncClient>.Instance), h);
    }

    [Fact]
    public async Task The_catalog_is_parsed_and_the_api_key_is_sent()
    {
        var (c, h) = Make();
        var s = await c.GetCatalogAsync();
        var folder = Assert.Single(Assert.Single(s.Plans).Folders);
        Assert.Equal("fo1", folder.ShareFileFolderId); Assert.Equal("Download", folder.Direction);
        Assert.False(s.Stale); Assert.Equal("test-key", h.ApiKey);
    }

    [Fact]
    public async Task A_second_read_inside_the_cache_time_does_not_call_FileSync_again_but_refresh_does()
    {
        var (c, h) = Make();
        await c.GetCatalogAsync(); await c.GetCatalogAsync();
        Assert.Equal(1, h.Calls);
        await c.GetCatalogAsync(refresh: true);
        Assert.Equal(2, h.Calls);
    }

    [Fact]
    public async Task When_FileSync_fails_the_last_good_catalog_is_returned_as_stale()
    {
        var (c, h) = Make();
        await c.GetCatalogAsync();
        h.Fail = true;
        var s = await c.GetCatalogAsync(refresh: true);
        Assert.True(s.Stale); Assert.Single(s.Plans); Assert.Contains("could not be reached", s.Error);
        h.Fail = false;
        Assert.False((await c.GetCatalogAsync(refresh: true)).Stale);   // recovers
    }

    [Fact]
    public async Task With_nothing_loaded_yet_a_failure_is_an_exception_and_a_retry_is_not_hammered()
    {
        var (c, h) = Make();
        h.Fail = true;
        await Assert.ThrowsAsync<FileSyncUnavailableException>(() => c.GetCatalogAsync());
        await Assert.ThrowsAsync<FileSyncUnavailableException>(() => c.GetCatalogAsync());
        Assert.Equal(1, h.Calls);   // the second call is inside the short back-off, so FileSync is not asked again
    }

    [Fact]
    public async Task A_garbled_answer_counts_as_unavailable()
    {
        var (c, h) = Make();
        h.Body = "<html>not json</html>";
        await Assert.ThrowsAsync<FileSyncUnavailableException>(() => c.GetCatalogAsync());
    }
}
