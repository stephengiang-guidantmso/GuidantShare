using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GuidantShare.Core;

namespace GuidantShare.Api.Services;

public class EmailOptions
{
    public string Mode { get; set; } = "Log";          // Graph | Log
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? Mailbox { get; set; }
    public string FromName { get; set; } = "GuidantShare";
    public string PublicBaseUrl { get; set; } = "http://localhost:5080";
}

/// <summary>Dev/test mode: writes the email to the log instead of sending it.</summary>
public class LogEmailSender : IEmailSender
{
    private readonly ILogger<LogEmailSender> _log;
    public LogEmailSender(ILogger<LogEmailSender> log) => _log = log;
    public string Mode => "Log";
    public Task<EmailResult> SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        _log.LogInformation("[Email:Log] To={To} Subject={Subject}\n{Body}", to, subject, body);
        return Task.FromResult(new EmailResult(true, false, null));
    }
}

public class GraphEmailSender : IEmailSender
{
    private readonly EmailOptions _o;
    private readonly HttpClient _http;
    private readonly ILogger<GraphEmailSender> _log;
    private string? _token; private DateTime _exp;
    public string Mode => "Graph";

    public GraphEmailSender(EmailOptions o, HttpClient http, ILogger<GraphEmailSender> log)
    {
        _o = o; _http = http; _log = log; _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_o.TenantId) && !string.IsNullOrWhiteSpace(_o.ClientId)
        && !string.IsNullOrWhiteSpace(_o.ClientSecret) && !string.IsNullOrWhiteSpace(_o.Mailbox);

    public async Task<string?> CheckAsync(CancellationToken ct = default)
    {
        if (!IsConfigured) return "Graph settings are incomplete.";
        try { await GetTokenAsync(ct); return null; } catch (Exception ex) { return ex.Message; }
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_token != null && DateTime.UtcNow < _exp) return _token;
        using var resp = await _http.PostAsync($"https://login.microsoftonline.com/{_o.TenantId}/oauth2/v2.0/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials", ["client_id"] = _o.ClientId!, ["client_secret"] = _o.ClientSecret!,
                ["scope"] = "https://graph.microsoft.com/.default"
            }), ct);
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"Graph token request failed ({(int)resp.StatusCode}).");
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        _token = doc.RootElement.GetProperty("access_token").GetString();
        _exp = DateTime.UtcNow.AddSeconds(doc.RootElement.GetProperty("expires_in").GetInt32() - 300);
        return _token!;
    }

    public async Task<EmailResult> SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        if (!IsConfigured) return new EmailResult(false, false, "Email is not configured.");
        try
        {
            var token = await GetTokenAsync(ct);
            var payload = new
            {
                message = new
                {
                    subject,
                    body = new { contentType = "Text", content = body },
                    toRecipients = new[] { new { emailAddress = new { address = to } } }
                },
                saveToSentItems = false
            };
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(_o.Mailbox!)}/sendMail")
            { Content = JsonContent.Create(payload) };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req, ct);
            var code = (int)resp.StatusCode;
            // Never log the body (contains the link). Log status and recipient domain only.
            _log.LogInformation("Graph sendMail status {Status} to domain {Domain}", code, to[(to.IndexOf('@') + 1)..]);
            return resp.StatusCode switch
            {
                HttpStatusCode.Accepted or HttpStatusCode.OK => new EmailResult(true, false, null),
                HttpStatusCode.BadRequest => new EmailResult(false, false, "That email address was rejected."),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new EmailResult(false, false,
                    "The wrapper isn't allowed to send mail. Check the Graph app permission and mailbox policy."),
                HttpStatusCode.NotFound => new EmailResult(false, false, "The sending mailbox doesn't exist."),
                HttpStatusCode.TooManyRequests => new EmailResult(false, true, "Microsoft is throttling email. Try again shortly."),
                _ when code >= 500 => new EmailResult(false, true, "Microsoft email service had a temporary error."),
                _ => new EmailResult(false, false, $"Email failed (HTTP {code}).")
            };
        }
        catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException)
        {
            if (ct.IsCancellationRequested) throw;
            _token = null;
            return new EmailResult(false, true, "Could not reach Microsoft email service.");
        }
    }
}
