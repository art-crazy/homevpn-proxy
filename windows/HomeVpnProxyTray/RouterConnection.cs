using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace HomeVpnProxyTray;

public sealed record RouterConnectionSettings(string Host, string Username, string Password);

/// <summary>
/// Stores the router's SSH credentials for the "check and fix" feature.
/// The password is encrypted with Windows DPAPI (CurrentUser scope)
/// before it touches disk - readable only by this Windows user account
/// on this machine, not in plain text, not portable to another PC or
/// user. Host/username aren't secret so they're stored as-is.
/// </summary>
internal static class RouterSettingsStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HomeVpnProxyTray",
        "router.json");

    private sealed record StoredFile(string Host, string Username, string EncryptedPasswordBase64);

    public static bool IsConfigured() => File.Exists(FilePath);

    public static RouterConnectionSettings? Load()
    {
        if (!File.Exists(FilePath)) return null;

        try
        {
            var stored = JsonSerializer.Deserialize<StoredFile>(File.ReadAllText(FilePath));
            if (stored is null) return null;

            var encrypted = Convert.FromBase64String(stored.EncryptedPasswordBase64);
            var passwordBytes = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return new RouterConnectionSettings(stored.Host, stored.Username, System.Text.Encoding.UTF8.GetString(passwordBytes));
        }
        catch
        {
            return null;
        }
    }

    public static void Save(string host, string username, string password)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

        var passwordBytes = System.Text.Encoding.UTF8.GetBytes(password);
        var encrypted = ProtectedData.Protect(passwordBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);

        var stored = new StoredFile(host, username, Convert.ToBase64String(encrypted));
        File.WriteAllText(FilePath, JsonSerializer.Serialize(stored));
    }

    public static void Clear()
    {
        if (File.Exists(FilePath)) File.Delete(FilePath);
    }
}
