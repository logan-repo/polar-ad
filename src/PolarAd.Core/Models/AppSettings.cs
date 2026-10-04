namespace PolarAd.Core.Models;

public sealed class AppSettings
{
    public bool ProtectionEnabled { get; set; } = false;
    public List<string> BlocklistSources { get; set; } = new()
    {
        "https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts",
        // AdGuard's Korean-language filter list (AdBlock Plus syntax), parsed by
        // AdblockRuleParser. Covers Korean ad/tracking networks that the mostly
        // Western-focused StevenBlack hosts list above misses (e.g. the ones
        // found testing against ruliweb.com / dcinside.com: adwiser.kr,
        // adsvani.com, cauly.co.kr, tpmn.io, ...).
        "https://filters.adtidy.org/extension/chromium/filters/227.txt",
    };
    public string UpstreamDnsPrimary { get; set; } = "1.1.1.1";
    public string UpstreamDnsSecondary { get; set; } = "1.0.0.1";
    public int DnsProxyPort { get; set; } = 53;
    public DateTimeOffset? LastBlocklistUpdate { get; set; }
    public int MaxLogEntries { get; set; } = 2000;
    public int AutoUpdateIntervalHours { get; set; } = 24;
}
