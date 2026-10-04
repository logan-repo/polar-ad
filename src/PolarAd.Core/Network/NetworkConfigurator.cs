using System.Diagnostics;
using System.Management;
using System.Text.Json;

namespace PolarAd.Core.Network;

public sealed class AdapterOriginalDns
{
    public string SettingId { get; set; } = "";
    public string Description { get; set; } = "";
    public bool WasDhcp { get; set; }
    public List<string> OriginalServers { get; set; } = new();
}

public sealed class DnsBackup
{
    public List<AdapterOriginalDns> Adapters { get; set; } = new();
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.Now;
}

/// <summary>
/// Points active network adapters at the local DNS proxy (127.0.0.1) and can restore
/// their original DNS configuration. This is the one part of PolarAd that changes
/// system-wide network settings, which is why the app requests administrator rights.
///
/// Safety: the very first time protection is enabled we snapshot the adapters' real
/// DNS settings to disk. Restore always reads from that snapshot, never from whatever
/// is currently configured, so a crash-while-enabled or an uninstall can always put
/// things back the way they were.
/// </summary>
public sealed class NetworkConfigurator
{
    private readonly string _backupFilePath;

    public NetworkConfigurator(string backupFilePath)
    {
        _backupFilePath = backupFilePath;
    }

    public bool HasPendingBackup => File.Exists(_backupFilePath);

    public List<AdapterOriginalDns> GetActiveAdapterConfigs()
    {
        var results = new List<AdapterOriginalDns>();
        using var searcher = new ManagementObjectSearcher(
            "SELECT SettingID, Description, DHCPEnabled, DNSServerSearchOrder, IPEnabled FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE");

        foreach (ManagementObject mo in searcher.Get())
        {
            var settingId = mo["SettingID"]?.ToString() ?? "";
            if (settingId.Length == 0) continue;

            var servers = (mo["DNSServerSearchOrder"] as string[])?.ToList() ?? new List<string>();
            results.Add(new AdapterOriginalDns
            {
                SettingId = settingId,
                Description = mo["Description"]?.ToString() ?? "",
                WasDhcp = (bool?)mo["DHCPEnabled"] ?? true,
                OriginalServers = servers,
            });
        }

        return results;
    }

    /// <summary>
    /// Snapshots current DNS config (if not already saved) and points every active
    /// adapter's DNS at 127.0.0.1. Must run elevated.
    /// </summary>
    public void EnableLoopbackDns()
    {
        if (!File.Exists(_backupFilePath))
        {
            var snapshot = new DnsBackup { Adapters = GetActiveAdapterConfigs() };
            Directory.CreateDirectory(Path.GetDirectoryName(_backupFilePath)!);
            File.WriteAllText(_backupFilePath, JsonSerializer.Serialize(snapshot));
        }

        using var searcher = new ManagementObjectSearcher(
            "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE");

        foreach (ManagementObject mo in searcher.Get())
        {
            try
            {
                using var inParams = mo.GetMethodParameters("SetDNSServerSearchOrder");
                inParams["DNSServerSearchOrder"] = new[] { "127.0.0.1" };
                mo.InvokeMethod("SetDNSServerSearchOrder", inParams, null);
            }
            catch
            {
                // Some virtual/disconnected adapters reject this; skip and continue with the rest.
            }
        }

        FlushDnsCache();
    }

    /// <summary>
    /// Restores DNS settings from the saved snapshot and deletes it. Safe to call
    /// multiple times; a missing snapshot is a no-op (nothing to restore).
    /// </summary>
    public void RestoreOriginalDns()
    {
        if (!File.Exists(_backupFilePath))
        {
            return;
        }

        DnsBackup? backup;
        try
        {
            backup = JsonSerializer.Deserialize<DnsBackup>(File.ReadAllText(_backupFilePath));
        }
        catch
        {
            backup = null;
        }

        if (backup != null)
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE");

            foreach (ManagementObject mo in searcher.Get())
            {
                var settingId = mo["SettingID"]?.ToString() ?? "";
                var original = backup.Adapters.FirstOrDefault(a => a.SettingId == settingId);
                if (original == null) continue;

                try
                {
                    if (original.WasDhcp || original.OriginalServers.Count == 0)
                    {
                        using var inParams = mo.GetMethodParameters("SetDNSServerSearchOrder");
                        inParams["DNSServerSearchOrder"] = null;
                        mo.InvokeMethod("SetDNSServerSearchOrder", inParams, null);
                    }
                    else
                    {
                        using var inParams = mo.GetMethodParameters("SetDNSServerSearchOrder");
                        inParams["DNSServerSearchOrder"] = original.OriginalServers.ToArray();
                        mo.InvokeMethod("SetDNSServerSearchOrder", inParams, null);
                    }
                }
                catch
                {
                    // Best-effort restore per-adapter; continue with the rest.
                }
            }
        }

        try { File.Delete(_backupFilePath); } catch { }
        FlushDnsCache();
    }

    public static void FlushDnsCache()
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo("ipconfig", "/flushdns")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            proc?.WaitForExit(5000);
        }
        catch { }
    }
}
