namespace PolarAd.Core.Models;

public enum BlockDecision
{
    Allowed,
    BlockedByList,
    BlockedByUserRule,
    AllowedByUserAllowlist,
}

public sealed class BlockLogEntry
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string Domain { get; set; } = "";
    public string AppName { get; set; } = "Unknown";
    public int ProcessId { get; set; }
    public BlockDecision Decision { get; set; }
    public string MatchedRule { get; set; } = "";
}
