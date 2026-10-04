namespace PolarAd.Core;

/// <summary>
/// All PolarAd state lives under %LocalAppData%\PolarAd. Nothing is ever written
/// outside this folder, and nothing here is uploaded anywhere.
/// </summary>
public static class AppPaths
{
    public static string RootDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PolarAd");

    public static string SettingsFile => Path.Combine(RootDir, "settings.json");
    public static string BlocklistCacheFile => Path.Combine(RootDir, "blocklist_cache.txt");
    public static string UserRulesFile => Path.Combine(RootDir, "user_rules.json");
    public static string AllowlistFile => Path.Combine(RootDir, "allowlist.json");
    public static string LogFile => Path.Combine(RootDir, "block_log.jsonl");
    public static string DnsBackupFile => Path.Combine(RootDir, "dns_backup.json");

    public static void EnsureCreated() => Directory.CreateDirectory(RootDir);
}
