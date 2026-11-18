using System.Security.Cryptography;

namespace GuidantShare.Core;

public interface ISecretStore
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, string value, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}

public interface IEmailSender
{
    Task<EmailResult> SendAsync(string to, string subject, string body, CancellationToken ct = default);
    string Mode { get; }
}

public record EmailResult(bool Success, bool Transient, string? Error);

public record SfItem(string Id, string Name, long Size, DateTime? Modified, bool IsFolder);

public record ShareFileStatus(bool Configured, bool Connected, string? Subdomain, string? ConnectedBy,
    DateTime? ConnectedUtc, string? Error, string? RedirectUri, string? ClientId = null);

public interface IShareFileService
{
    Task<ShareFileStatus> GetStatusAsync(CancellationToken ct = default);
    Task SaveAppSettingsAsync(string subdomain, string clientId, string? clientSecret, string? redirectUri, CancellationToken ct = default);
    Task<string> BuildAuthorizeUrlAsync(string state, CancellationToken ct = default);
    Task ConnectWithCodeAsync(string code, string connectedBy, CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
    Task<string?> ResolvePathAsync(string path, CancellationToken ct = default);
    Task<IReadOnlyList<SfItem>> ListChildrenAsync(string folderId, CancellationToken ct = default);
    Task<SfItem?> GetItemAsync(string id, CancellationToken ct = default);
    Task<SfItem> UploadAsync(string folderId, string fileName, Stream content, long length, bool overwrite, CancellationToken ct = default);
    Task<string> GetWebLinkAsync(string itemId, CancellationToken ct = default);
}

public class ShareFileNotConnectedException : Exception
{
    public ShareFileNotConnectedException() : base("ShareFile is disconnected. Ask a SuperAdmin to reconnect.") { }
}

/// <summary>AES-256-GCM. Stored blob = nonce(12) | tag(16) | ciphertext.</summary>
public class AesGcmCipher
{
    private readonly byte[] _key;
    public AesGcmCipher(byte[] key)
    {
        if (key.Length != 32) throw new ArgumentException("Key must be 32 bytes");
        _key = key;
    }

    public byte[] Encrypt(string plaintext)
    {
        var data = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[data.Length];
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, data, cipher, tag);
        return nonce.Concat(tag).Concat(cipher).ToArray();
    }

    public string Decrypt(byte[] blob)
    {
        var nonce = blob[..12];
        var tag = blob[12..28];
        var cipher = blob[28..];
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return System.Text.Encoding.UTF8.GetString(plain);
    }

    /// <summary>Loads (or creates) the wrapper's own master key. DPAPI-protected on Windows.</summary>
    public static AesGcmCipher LoadOrCreate(string keyPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(keyPath))!);
        byte[] key;
        if (File.Exists(keyPath))
        {
            var raw = File.ReadAllBytes(keyPath);
            key = OperatingSystem.IsWindows() ? Unprotect(raw) : raw;
        }
        else
        {
            key = RandomNumberGenerator.GetBytes(32);
            File.WriteAllBytes(keyPath, OperatingSystem.IsWindows() ? Protect(key) : key);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        return new AesGcmCipher(key);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static byte[] Protect(byte[] d) => ProtectedData.Protect(d, null, DataProtectionScope.LocalMachine);
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static byte[] Unprotect(byte[] d) => ProtectedData.Unprotect(d, null, DataProtectionScope.LocalMachine);
}
