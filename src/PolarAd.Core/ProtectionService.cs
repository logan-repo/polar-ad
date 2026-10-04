using System.Net;
using PolarAd.Core.Blocklist;
using PolarAd.Core.Dns;
using PolarAd.Core.Logging;
using PolarAd.Core.Models;
using PolarAd.Core.Network;
using PolarAd.Core.Rules;
using PolarAd.Core.Settings;

namespace PolarAd.Core;

/// <summary>
/// Top-level orchestrator: wires the rule engine, DNS proxy, network configurator,
/// blocklist updater, and log store together. This is what the UI talks to.
/// </summary>
public sealed class ProtectionService : IAsyncDisposable
{
    public AppSettings Settings { get; private set; }
    public RuleEngine RuleEngine { get; } = new();
    public BlockLogStore LogStore { get; }
    public bool IsRunning { get; private set; }

    private readonly NetworkConfigurator _networkConfigurator;
    private readonly BlocklistUpdater _blocklistUpdater;
    private readonly HttpClient _httpClient;
    private DnsProxyServer? _dnsProxy;
    private CancellationTokenSource? _autoUpdateCts;

    public event Action<DnsQueryObserved>? QueryObserved;
    public event Action<BlocklistUpdateResult>? AutoUpdateCompleted;

    public ProtectionService()
    {
        AppPaths.EnsureCreated();
        Settings = SettingsStore.Load(AppPaths.SettingsFile);
        LogStore = new BlockLogStore(AppPaths.LogFile, Settings.MaxLogEntries);
        _networkConfigurator = new NetworkConfigurator(AppPaths.DnsBackupFile);
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _blocklistUpdater = new BlocklistUpdater(_httpClient, AppPaths.BlocklistCacheFile);

        // Only the small, synchronous-by-nature files load here. The blocklist
        // cache can be tens of thousands of lines, so it's loaded (and awaited)
        // in ReconcileStartupStateAsync instead — see the comment there for why
        // doing it fire-and-forget here was a real bug.
        RuleEngine.UserBlockRules.ReplaceAll(SettingsStore.LoadDomainList(AppPaths.UserRulesFile));
        RuleEngine.Allowlist.ReplaceAll(SettingsStore.LoadDomainList(AppPaths.AllowlistFile));
    }

    /// <summary>
    /// Call once at app startup. Detects an unclean previous shutdown (a DNS backup
    /// file exists but protection wasn't recorded as enabled) and restores the
    /// system's real DNS settings defensively.
    /// </summary>
    public async Task ReconcileStartupStateAsync()
    {
        // Must be awaited (not fire-and-forget) BEFORE the UI first reads
        // RuleEngine.PublicBlocklist.Count — otherwise the dashboard renders
        // "차단 도메인 0개" from the still-empty in-memory set and nothing ever
        // triggers a re-render once the cache file actually finishes loading,
        // so it looks like the blocklist resets to empty on every launch even
        // though previously-downloaded domains are sitting right there on disk.
        try
        {
            await _blocklistUpdater.LoadFromCacheAsync(RuleEngine.PublicBlocklist);
        }
        catch
        {
            // Missing/corrupt cache just means "지금 차단 목록 업데이트" is needed; not fatal.
        }

        if (_networkConfigurator.HasPendingBackup && !Settings.ProtectionEnabled)
        {
            try
            {
                _networkConfigurator.RestoreOriginalDns();
            }
            catch
            {
                // If this fails, the "Restore network settings" action in the UI
                // and the README's manual recovery steps are the fallback.
            }
        }

        if (Settings.ProtectionEnabled)
        {
            await StartAsync();
        }

        StartAutoUpdateLoop();
    }

    /// <summary>
    /// Runs for the lifetime of the app: periodically re-checks whether the
    /// blocklist is older than <see cref="AppSettings.AutoUpdateIntervalHours"/>
    /// and refreshes it if so. Checked hourly rather than sleeping for the full
    /// interval so a change to the interval in Settings takes effect without
    /// restarting the app.
    /// </summary>
    private void StartAutoUpdateLoop()
    {
        if (_autoUpdateCts != null) return;

        _autoUpdateCts = new CancellationTokenSource();
        var ct = _autoUpdateCts.Token;

        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var interval = TimeSpan.FromHours(Math.Max(1, Settings.AutoUpdateIntervalHours));
                    var due = Settings.LastBlocklistUpdate == null
                        || DateTimeOffset.Now - Settings.LastBlocklistUpdate.Value >= interval;

                    if (due)
                    {
                        var result = await UpdateBlocklistAsync(ct);
                        AutoUpdateCompleted?.Invoke(result);
                    }
                }
                catch
                {
                    // Transient network failure — next hourly check will retry.
                }

                try
                {
                    await Task.Delay(TimeSpan.FromHours(1), ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, ct);
    }

    public async Task<BlocklistUpdateResult> UpdateBlocklistAsync(CancellationToken ct = default)
    {
        var result = await _blocklistUpdater.UpdateAsync(Settings.BlocklistSources, RuleEngine.PublicBlocklist, ct);
        if (result.Success)
        {
            Settings.LastBlocklistUpdate = DateTimeOffset.Now;
            SettingsStore.Save(AppPaths.SettingsFile, Settings);

            // Newly-blocked domains resolved before this update (cached in the OS
            // resolver cache, and separately in the browser's own in-memory DNS
            // cache) would otherwise keep working until their TTL expires.
            NetworkConfigurator.FlushDnsCache();
        }
        return result;
    }

    /// <summary>
    /// Starts the local DNS proxy and (if elevated) points the system's active
    /// network adapters at it. Requires administrator rights for the DNS change;
    /// if that fails, the proxy still runs but nothing routes traffic to it yet.
    /// </summary>
    public Task<bool> StartAsync()
    {
        if (IsRunning) return Task.FromResult(true);

        _dnsProxy = new DnsProxyServer(
            RuleEngine,
            new IPEndPoint(IPAddress.Parse(Settings.UpstreamDnsPrimary), 53),
            Settings.DnsProxyPort);

        _dnsProxy.QueryObserved += OnQueryObserved;

        try
        {
            _dnsProxy.Start();
        }
        catch (Exception)
        {
            _dnsProxy.QueryObserved -= OnQueryObserved;
            _dnsProxy = null;
            return Task.FromResult(false);
        }

        try
        {
            _networkConfigurator.EnableLoopbackDns();
        }
        catch
        {
            // Not elevated, or WMI call failed — the proxy keeps running so the
            // UI can still show it's listening, but system traffic won't reach it
            // until the app is run as Administrator.
        }

        IsRunning = true;
        Settings.ProtectionEnabled = true;
        SettingsStore.Save(AppPaths.SettingsFile, Settings);
        return Task.FromResult(true);
    }

    /// <summary>
    /// The user turned protection off. Clears the persisted "keep protection on"
    /// intent so it won't resume on next launch.
    /// </summary>
    public async Task StopAsync()
    {
        await TearDownRuntimeAsync();
        Settings.ProtectionEnabled = false;
        SettingsStore.Save(AppPaths.SettingsFile, Settings);
    }

    /// <summary>
    /// The app is exiting (tray Exit, logoff, shutdown). DNS must be restored so the
    /// machine isn't left pointing at a proxy that's about to disappear, but the
    /// persisted "keep protection on" intent is deliberately left alone so protection
    /// resumes automatically the next time the app starts.
    /// </summary>
    public Task ShutdownAsync() => TearDownRuntimeAsync();

    private async Task TearDownRuntimeAsync()
    {
        if (_dnsProxy != null)
        {
            _dnsProxy.QueryObserved -= OnQueryObserved;
            await _dnsProxy.StopAsync();
            _dnsProxy = null;
        }

        try
        {
            _networkConfigurator.RestoreOriginalDns();
        }
        catch
        {
            // Surfaced to the user via the UI; manual recovery steps are in the README.
        }

        IsRunning = false;
    }

    /// <summary>
    /// Emergency recovery: stop everything and force-restore DNS from the backup,
    /// regardless of current running state. Used by the "Fix my network" action.
    /// </summary>
    public async Task EmergencyRestoreAsync()
    {
        if (_dnsProxy != null)
        {
            _dnsProxy.QueryObserved -= OnQueryObserved;
            await _dnsProxy.StopAsync();
            _dnsProxy = null;
        }

        _networkConfigurator.RestoreOriginalDns();
        IsRunning = false;
        Settings.ProtectionEnabled = false;
        SettingsStore.Save(AppPaths.SettingsFile, Settings);
    }

    public void AddUserBlockRule(string domain)
    {
        RuleEngine.UserBlockRules.Add(domain);
        SettingsStore.SaveDomainList(AppPaths.UserRulesFile, RuleEngine.UserBlockRules.All());
    }

    public void RemoveUserBlockRule(string domain)
    {
        RuleEngine.UserBlockRules.Remove(domain);
        SettingsStore.SaveDomainList(AppPaths.UserRulesFile, RuleEngine.UserBlockRules.All());
    }

    public void AddAllowlistEntry(string domain)
    {
        RuleEngine.Allowlist.Add(domain);
        SettingsStore.SaveDomainList(AppPaths.AllowlistFile, RuleEngine.Allowlist.All());
        NetworkConfigurator.FlushDnsCache();
    }

    public void RemoveAllowlistEntry(string domain)
    {
        RuleEngine.Allowlist.Remove(domain);
        SettingsStore.SaveDomainList(AppPaths.AllowlistFile, RuleEngine.Allowlist.All());
    }

    private void OnQueryObserved(DnsQueryObserved q)
    {
        LogStore.Add(new BlockLogEntry
        {
            Domain = q.Domain,
            AppName = q.AppName,
            ProcessId = q.ProcessId,
            Decision = q.Decision,
            MatchedRule = q.MatchedRule,
        });
        QueryObserved?.Invoke(q);
    }

    public async ValueTask DisposeAsync()
    {
        _autoUpdateCts?.Cancel();
        _autoUpdateCts = null;

        if (_dnsProxy != null)
        {
            await _dnsProxy.DisposeAsync();
        }
        _httpClient.Dispose();
    }
}
