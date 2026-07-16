using Microsoft.Win32;

namespace HomeVpnProxyTray;

internal static class AutoStartManager
{
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Constants.RunRegPath, writable: false);
        return key?.GetValue(Constants.AutoStartValueName) is string existing &&
               string.Equals(existing.Trim('"'), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Constants.RunRegPath, writable: true);
        if (key is null) return;

        if (enabled)
        {
            var exePath = Environment.ProcessPath;
            if (exePath is not null)
            {
                key.SetValue(Constants.AutoStartValueName, $"\"{exePath}\"", RegistryValueKind.String);
            }
        }
        else
        {
            key.DeleteValue(Constants.AutoStartValueName, throwOnMissingValue: false);
        }
    }
}
