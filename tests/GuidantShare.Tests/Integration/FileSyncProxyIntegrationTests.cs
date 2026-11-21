using System.Net;
using System.Text;
using GuidantShare.Core;
using GuidantShare.Tests.Support;

namespace GuidantShare.Tests.Integration;

/// <summary>The SuperAdmin-only proxy to FileSync: who may use it, what it allows, what it refuses and what it records.</summary>
[Trait("Category", "Integration")]
public class FileSyncProxyIntegrationTests : IAsyncLifetime
{
    private readonly TestApp _app = new(new FakeShareFile());
    private ApiClient _admin = null!, _user = null!;

    public async Task InitializeAsync()
    {
        await _app.AddUserAsync("admin@test.local", Roles.SuperAdmin); await _app.AddUserAsync("user@test.local", Roles.User);
        _admin = _app.NewClient(); await _admin.Login("admin@test.local");
        _user = _app.NewClient(); await _user.Login("user@test.local");
    }
    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task Signed_out_gets_401_and_a_normal_user_gets_403_and_FileSync_is_never_called()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.NewClient().Get("/api/filesync/workflows")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await _user.Get("/api/filesync/workflows")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await _user.Delete("/api/filesync/transfers")).Status);
        Assert.Empty(_app.FileSync.Calls);
    }

    [Fact]
    public async Task A_superadmin_reaches_allowed_calls_with_the_query_string()
    {
        var r = await _admin.Get("/api/filesync/transfers?limit=5&status=Failed");
        Assert.Equal(HttpStatusCode.OK, r.Status);
        var call = Assert.Single(_app.FileSync.Calls);
        Assert.Equal("GET", call.Method); Assert.Equal("api/transfers?limit=5&status=Failed", call.Path);
    }

    [Theory]
    [InlineData("GET", "workflows/wf-aetna-1/credentials")]
    [InlineData("GET", "debug/state")]
    [InlineData("GET", "staging")]
    [InlineData("GET", "../swagger")]
    [InlineData("GET", "swagger/v1/swagger.json")]
    [InlineData("GET", "workflows/%2e%2e/credentials")]
    [InlineData("PATCH", "workflows")]
    [InlineData("PUT", "workflows")]
    [InlineData("GET", "workflows/a/b/c")]
    public async Task Everything_not_on_the_allow_list_is_refused_and_never_forwarded(string method, string path)
    {
        var r = method == "GET" ? await _admin.Get("/api/filesync/" + path) : await _admin.Send(new HttpMethod(method), "/api/filesync/" + path, new { });
        Assert.NotEqual(HttpStatusCode.OK, r.Status);
        Assert.Empty(_app.FileSync.Calls);
    }

    [Fact]
    public async Task Changes_need_a_csrf_token()
    {
        var r = await _admin.PostWithoutCsrf("/api/filesync/workflows", new { name = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, r.Status);
        Assert.Empty(_app.FileSync.Calls);
    }

    [Fact]
    public async Task Changes_are_audited_and_reads_and_portal_codes_are_not()
    {
        await _admin.Get("/api/filesync/workflows");
        await _admin.Post("/api/filesync/portal-code", new { code = "123456" });
        Assert.Equal(2, _app.FileSync.Calls.Count);
        Assert.Empty(_app.Query(db => db.AdminAudits.ToList()));

        await _admin.Post("/api/filesync/workflows", new { name = "New job" });
        await _admin.Post("/api/filesync/workflows/wf-1/run", new { mode = "Live" });
        var audits = _app.Query(db => db.AdminAudits.OrderBy(a => a.Id).ToList());
        Assert.Equal(new[] { "POST workflows", "POST workflows/wf-1/run" }, audits.Select(a => a.Action));
        Assert.All(audits, a => { Assert.Equal(200, a.Status); Assert.NotNull(a.UserId); });
        Assert.DoesNotContain("New job", string.Join("", audits.Select(a => a.Action)));   // bodies are never stored
    }

    [Fact]
    public async Task The_request_body_and_content_type_reach_FileSync()
    {
        string? seenBody = null, seenType = null;
        _app.FileSync.Handler = req =>
        {
            seenBody = req.Content!.ReadAsStringAsync().Result; seenType = req.Content.Headers.ContentType?.MediaType;
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{\"workflowId\":\"wf-1\"}", Encoding.UTF8, "application/json") };
        };
        var r = await _admin.Post("/api/filesync/workflows", new { name = "Aetna", plan = "Aetna" });
        Assert.Equal(HttpStatusCode.Created, r.Status);
        Assert.Equal("wf-1", r.Json.GetProperty("workflowId").GetString());
        Assert.Contains("\"plan\":\"Aetna\"", seenBody); Assert.Equal("application/json", seenType);
    }

    [Fact]
    public async Task FileSync_errors_and_downloads_pass_through()
    {
        _app.FileSync.Handler = req => req.RequestUri!.ToString().Contains("csv")
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("a,b\n1,2\n"))
                { Headers = { ContentType = new("text/csv"), ContentDisposition = new("attachment") { FileName = "verify.csv" } } }
            }
            : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"message\":\"Workflow x not found.\"}", Encoding.UTF8, "application/json") };

        var missing = await _admin.Get("/api/filesync/workflows/x/verifications/latest");
        Assert.Equal(HttpStatusCode.NotFound, missing.Status); Assert.Equal("Workflow x not found.", missing.Json.GetProperty("message").GetString());

        var csv = await _admin.Raw("/api/filesync/verifications/abc123/csv");
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode); Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        Assert.Equal("a,b\n1,2\n", await csv.Content.ReadAsStringAsync());
        Assert.Equal("verify.csv", csv.Content.Headers.ContentDisposition?.FileName);
    }

    [Fact]
    public async Task When_FileSync_is_down_the_proxy_answers_502()
    {
        _app.FileSync.Down = true;
        var r = await _admin.Get("/api/filesync/workflows");
        Assert.Equal(HttpStatusCode.BadGateway, r.Status);
    }
}
