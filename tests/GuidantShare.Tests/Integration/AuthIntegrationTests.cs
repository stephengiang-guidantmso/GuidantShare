using System.Net;
using GuidantShare.Core;
using GuidantShare.Tests.Support;

namespace GuidantShare.Tests.Integration;

/// <summary>The real app hosted in-process with ShareFile mocked: sign-in, roles, CSRF and invites.</summary>
[Trait("Category", "Integration")]
public class AuthIntegrationTests : IDisposable
{
    private readonly FakeShareFile _sf = new();
    private readonly TestApp _app;
    public AuthIntegrationTests() => _app = new TestApp(_sf);
    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Health_reports_the_mocked_sharefile_as_connected()
    {
        var r = await _app.NewClient().Get("/health");
        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.Equal("connected", r.Json.GetProperty("sharefile").GetString());
    }

    [Fact]
    public async Task Everything_under_api_needs_a_login()
    {
        var c = _app.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.Get("/api/plans")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.Get("/api/entries/1/files")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.Get("/api/catalog")).Status);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_user_get_the_same_answer()
    {
        await _app.AddUserAsync("a@test.local", Roles.User);
        var c = _app.NewClient();
        var wrong = await c.Login("a@test.local", "Wrong-password-1");
        var unknown = await c.Login("nobody@test.local");
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.Status); Assert.Equal(HttpStatusCode.Unauthorized, unknown.Status);
        Assert.Equal(wrong.Error, unknown.Error);
    }

    [Fact]
    public async Task Login_returns_the_role_and_me_works_afterwards()
    {
        await _app.AddUserAsync("admin@test.local", Roles.SuperAdmin);
        var c = _app.NewClient();
        var login = await c.Login("admin@test.local");
        Assert.Equal(HttpStatusCode.OK, login.Status); Assert.Equal(Roles.SuperAdmin, login.Json.GetProperty("role").GetString());
        var me = await c.Get("/api/auth/me");
        Assert.Equal("admin@test.local", me.Json.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Writes_without_a_csrf_token_are_refused()
    {
        await _app.AddUserAsync("a@test.local", Roles.User);
        var r = await _app.NewClient().PostWithoutCsrf("/api/auth/login", new { email = "a@test.local", password = TestApp.Password });
        Assert.Equal(HttpStatusCode.BadRequest, r.Status); Assert.Contains("CSRF", r.Error);
    }

    [Fact]
    public async Task Only_superadmins_reach_settings_and_user_management()
    {
        await _app.AddUserAsync("user@test.local", Roles.User);
        var c = _app.NewClient(); await c.Login("user@test.local");
        foreach (var path in new[] { "/api/catalog", "/api/users", "/api/sharefile/status" })
            Assert.Equal(HttpStatusCode.Forbidden, (await c.Get(path)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.Post("/api/catalog/plans", new { name = "X" })).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.Post("/api/invites", new { email = "b@test.local", role = "User" })).Status);
    }

    [Fact]
    public async Task A_superadmin_can_invite_and_the_invite_works_exactly_once()
    {
        await _app.AddUserAsync("admin@test.local", Roles.SuperAdmin);
        var admin = _app.NewClient(); await admin.Login("admin@test.local");

        var inv = await admin.Post("/api/invites", new { email = "new@test.local", role = "User", noEmail = true, emailOnly = false });
        Assert.Equal(HttpStatusCode.OK, inv.Status);
        var link = inv.Json.GetProperty("inviteLink").GetString()!;
        var token = link[(link.IndexOf("token=") + 6)..];
        Assert.StartsWith("http://test.local/accept-invite", link);

        var fresh = _app.NewClient();
        Assert.Equal("new@test.local", (await fresh.Get($"/api/auth/invite-info?token={token}")).Json.GetProperty("email").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await fresh.Post("/api/auth/accept-invite", new { token, password = "short" })).Status);   // password rules apply
        Assert.Equal(HttpStatusCode.OK, (await fresh.Post("/api/auth/accept-invite", new { token, password = TestApp.Password })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await fresh.Post("/api/auth/accept-invite", new { token, password = TestApp.Password })).Status);   // single use
        Assert.Equal(HttpStatusCode.OK, (await _app.NewClient().Login("new@test.local")).Status);
    }

    [Fact]
    public async Task Inviting_by_email_sends_one_mail_and_never_returns_the_link()
    {
        await _app.AddUserAsync("admin@test.local", Roles.SuperAdmin);
        var admin = _app.NewClient(); await admin.Login("admin@test.local");
        var inv = await admin.Post("/api/invites", new { email = "mail@test.local", role = "User", emailOnly = true });
        Assert.Equal(HttpStatusCode.OK, inv.Status);
        Assert.Equal(JsonValueKindNull, inv.Json.GetProperty("inviteLink").ValueKind);
        var mail = Assert.Single(_app.Email.Sent);
        Assert.Equal("mail@test.local", mail.To); Assert.Contains("/accept-invite?token=", mail.Body);
    }
    private const System.Text.Json.JsonValueKind JsonValueKindNull = System.Text.Json.JsonValueKind.Null;

    [Fact]
    public async Task Duplicate_and_invalid_invites_are_rejected()
    {
        await _app.AddUserAsync("admin@test.local", Roles.SuperAdmin); await _app.AddUserAsync("exists@test.local", Roles.User);
        var admin = _app.NewClient(); await admin.Login("admin@test.local");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Post("/api/invites", new { email = "exists@test.local", role = "User", noEmail = true, emailOnly = false })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Post("/api/invites", new { email = "not-an-email", role = "User", noEmail = true, emailOnly = false })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Post("/api/invites", new { email = "x@test.local", role = "Root", noEmail = true, emailOnly = false })).Status);
    }

    [Fact]
    public async Task A_deactivated_user_can_no_longer_sign_in_and_admins_cannot_deactivate_themselves()
    {
        await _app.AddUserAsync("admin@test.local", Roles.SuperAdmin); await _app.AddUserAsync("user@test.local", Roles.User);
        var admin = _app.NewClient(); await admin.Login("admin@test.local");
        var users = (await admin.Get("/api/users")).Json.GetProperty("users").EnumerateArray().ToList();
        string IdOf(string email) => users.First(u => u.GetProperty("email").GetString() == email).GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Patch($"/api/users/{IdOf("admin@test.local")}", new { isActive = false })).Status);
        Assert.Equal(HttpStatusCode.OK, (await admin.Patch($"/api/users/{IdOf("user@test.local")}", new { isActive = false })).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.NewClient().Login("user@test.local")).Status);
    }
}
