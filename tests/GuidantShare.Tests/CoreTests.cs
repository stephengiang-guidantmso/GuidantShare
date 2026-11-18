using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using GuidantShare.Api;
using GuidantShare.Api.Services;
using GuidantShare.Core;
using Xunit;

namespace GuidantShare.Tests;

[Trait("Category", "Unit")]
public class CoreTests
{
    [Fact]
    public void Cipher_roundtrips_and_detects_tampering()
    {
        var c = new AesGcmCipher(RandomNumberGenerator.GetBytes(32));
        var blob = c.Encrypt("secret-value");
        Assert.Equal("secret-value", c.Decrypt(blob));
        blob[^1] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => c.Decrypt(blob));
    }

    [Theory]
    [InlineData("report.csv", "report.csv")]
    [InlineData("..\\..\\evil.txt", "evil.txt")]
    [InlineData("a/b/c.txt", "c.txt")]
    [InlineData("", null)]
    [InlineData("..", null)]
    public void SanitizeName(string input, string? expected) => Assert.Equal(expected, EndpointsExt.SanitizeName(input));

    [Fact]
    public void Invite_token_hash_is_stable_and_not_the_token()
    {
        var t = InviteService.NewToken();
        Assert.Equal(64, t.Length);
        Assert.Equal(InviteService.Hash(t), InviteService.Hash(t));
        Assert.NotEqual(t, InviteService.Hash(t));
    }

    private class Fake(HttpStatusCode mailStatus) : HttpMessageHandler
    {
        public int MailCalls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            if (r.RequestUri!.Host.Contains("login.microsoftonline"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"t\",\"expires_in\":3600}") });
            MailCalls++;
            return Task.FromResult(new HttpResponseMessage(mailStatus));
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.Accepted, true, false, null)]
    [InlineData(HttpStatusCode.BadRequest, false, false, "rejected")]
    [InlineData(HttpStatusCode.Unauthorized, false, false, "allowed to send")]
    [InlineData(HttpStatusCode.Forbidden, false, false, "allowed to send")]
    [InlineData(HttpStatusCode.NotFound, false, false, "mailbox")]
    [InlineData(HttpStatusCode.TooManyRequests, false, true, "throttling")]
    [InlineData(HttpStatusCode.BadGateway, false, true, "temporary")]
    public async Task Graph_status_mapping(HttpStatusCode code, bool ok, bool transient, string? text)
    {
        var o = new EmailOptions { Mode = "Graph", TenantId = "t", ClientId = "c", ClientSecret = "s", Mailbox = "m@x.com" };
        var s = new GraphEmailSender(o, new HttpClient(new Fake(code)), NullLogger<GraphEmailSender>.Instance);
        var r = await s.SendAsync("a@b.com", "s", "b");
        Assert.Equal(ok, r.Success); Assert.Equal(transient, r.Transient);
        if (text != null) Assert.Contains(text, r.Error);
    }
}
