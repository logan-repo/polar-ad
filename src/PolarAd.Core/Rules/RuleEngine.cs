using PolarAd.Core.Models;

namespace PolarAd.Core.Rules;

public sealed class RuleDecisionResult
{
    public bool Blocked { get; init; }
    public BlockDecision Decision { get; init; }
    public string MatchedRule { get; init; } = "";
}

/// <summary>
/// Combines the public blocklist, built-in rules, user block rules, and the user allowlist
/// into a single block/allow decision. Precedence: allowlist > user rules > built-in rules > public blocklist.
/// </summary>
public sealed class RuleEngine
{
    private readonly DomainSet _publicBlocklist = new();
    private readonly DomainSet _userBlockRules = new();
    private readonly DomainSet _allowlist = new();
    private readonly DomainSet _builtInRules = new();

    public DomainSet PublicBlocklist => _publicBlocklist;
    public DomainSet UserBlockRules => _userBlockRules;
    public DomainSet Allowlist => _allowlist;
    public DomainSet BuiltInRules => _builtInRules;

    public RuleEngine()
    {
        _builtInRules.ReplaceAll(PolarAd.Core.Rules.BuiltInRules.Domains);
    }

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

        var builtInMatch = _builtInRules.FindMatch(domain);
        if (builtInMatch != null)
        {
            return new RuleDecisionResult
            {
                Blocked = true,
                Decision = BlockDecision.BlockedByBuiltIn,
                MatchedRule = builtInMatch,
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
