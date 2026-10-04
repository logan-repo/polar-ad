using PolarAd.Core.Models;

namespace PolarAd.Core.Rules;

public sealed class RuleDecisionResult
{
    public bool Blocked { get; init; }
    public BlockDecision Decision { get; init; }
    public string MatchedRule { get; init; } = "";
}

/// <summary>
/// Combines the public blocklist, user custom block rules, and the user allowlist
/// into a single block/allow decision. Precedence: allowlist > user block rules > public blocklist.
/// </summary>
public sealed class RuleEngine
{
    private readonly DomainSet _publicBlocklist = new();
    private readonly DomainSet _userBlockRules = new();
    private readonly DomainSet _allowlist = new();

    public DomainSet PublicBlocklist => _publicBlocklist;
    public DomainSet UserBlockRules => _userBlockRules;
    public DomainSet Allowlist => _allowlist;

    public RuleDecisionResult Evaluate(string domain)
    {
        var allowMatch = _allowlist.FindMatch(domain);
        if (allowMatch != null)
        {
            return new RuleDecisionResult
            {
                Blocked = false,
                Decision = BlockDecision.AllowedByUserAllowlist,
                MatchedRule = allowMatch,
            };
        }

        var userMatch = _userBlockRules.FindMatch(domain);
        if (userMatch != null)
        {
            return new RuleDecisionResult
            {
                Blocked = true,
                Decision = BlockDecision.BlockedByUserRule,
                MatchedRule = userMatch,
            };
        }

        var listMatch = _publicBlocklist.FindMatch(domain);
        if (listMatch != null)
        {
            return new RuleDecisionResult
            {
                Blocked = true,
                Decision = BlockDecision.BlockedByList,
                MatchedRule = listMatch,
            };
        }

        return new RuleDecisionResult
        {
            Blocked = false,
            Decision = BlockDecision.Allowed,
            MatchedRule = "",
        };
    }
}
