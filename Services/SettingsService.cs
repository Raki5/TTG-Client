using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TTGClient.Models;

namespace TTGClient.Services;

public sealed class SettingsService
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TTGClient|CurseForgeApiKey|v1");
    private readonly string _settingsPath;
    private AppSettings _settings;

    public SettingsService()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TTGClient");
        Directory.CreateDirectory(folder);
        _settingsPath = Path.Combine(folder, "settings.json");
        _settings = Load();
    }

    public string GetCurseForgeApiKey()
    {
        var env = Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY");
        if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
        if (string.IsNullOrWhiteSpace(_settings.ProtectedCurseForgeApiKey)) return string.Empty;

        try
        {
            var cipher = Convert.FromBase64String(_settings.ProtectedCurseForgeApiKey);
            var clear = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(clear);
        }
        catch
        {
            return string.Empty;
        }
    }

    public void SaveCurseForgeApiKey(string key)
    {
        key = key.Trim();
        if (string.IsNullOrEmpty(key))
        {
            _settings.ProtectedCurseForgeApiKey = null;
        }
        else
        {
            var clear = Encoding.UTF8.GetBytes(key);
            var protectedBytes = ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser);
            _settings.ProtectedCurseForgeApiKey = Convert.ToBase64String(protectedBytes);
        }
        Save();
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath)) ?? new AppSettings();
        }
        catch { /* A malformed settings file should not prevent the app from opening. */ }
        return new AppSettings();
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsPath, json, Encoding.UTF8);
    }
}
