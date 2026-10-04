using PolarAd.Core.Models;
using PolarAd.Core.Rules;

namespace PolarAd.Tests;

public class RuleEngineTests
{
    [Fact]
    public void Allowed_when_no_rules_match()
    {
        var engine = new RuleEngine();
        var result = engine.Evaluate("example.com");
        Assert.False(result.Blocked);
        Assert.Equal(BlockDecision.Allowed, result.Decision);
    }

    [Fact]
    public void Blocked_by_public_blocklist_exact_match()
    {
        var engine = new RuleEngine();
        engine.PublicBlocklist.Add("doubleclick.net");

        var result = engine.Evaluate("doubleclick.net");
        Assert.True(result.Blocked);
        Assert.Equal(BlockDecision.BlockedByList, result.Decision);
    }

    [Fact]
    public void Blocked_by_public_blocklist_subdomain_match()
    {
        var engine = new RuleEngine();
        engine.PublicBlocklist.Add("doubleclick.net");

        var result = engine.Evaluate("ads.doubleclick.net");
        Assert.True(result.Blocked);
        Assert.Equal("doubleclick.net", result.MatchedRule);
    }

    [Fact]
    public void User_block_rule_blocks_domain()
    {
        var engine = new RuleEngine();
        engine.UserBlockRules.Add("annoying-tracker.example");

        var result = engine.Evaluate("annoying-tracker.example");
        Assert.True(result.Blocked);
        Assert.Equal(BlockDecision.BlockedByUserRule, result.Decision);
    }

    [Fact]
    public void Allowlist_overrides_public_blocklist()
    {
        var engine = new RuleEngine();
        engine.PublicBlocklist.Add("example.com");
        engine.Allowlist.Add("example.com");

        var result = engine.Evaluate("example.com");
        Assert.False(result.Blocked);
        Assert.Equal(BlockDecision.AllowedByUserAllowlist, result.Decision);
    }

    [Fact]
    public void Allowlist_overrides_user_block_rule()
    {
        var engine = new RuleEngine();
        engine.UserBlockRules.Add("news.example");
        engine.Allowlist.Add("news.example");

        var result = engine.Evaluate("news.example");
        Assert.False(result.Blocked);
    }

    [Fact]
    public void Allowlist_subdomain_scoping_does_not_allow_unrelated_domain()
    {
        var engine = new RuleEngine();
        engine.PublicBlocklist.Add("ads.example.com");
        engine.Allowlist.Add("shop.example.com");

        var result = engine.Evaluate("ads.example.com");
        Assert.True(result.Blocked);
    }

    [Fact]
    public void Built_in_rules_are_always_applied()
    {
        var engine = new RuleEngine();
        var result = engine.Evaluate("image.ruliweb.com");
        Assert.True(result.Blocked);
        Assert.Equal(BlockDecision.BlockedByBuiltIn, result.Decision);
    }

    [Fact]
    public void Allowlist_overrides_built_in_rules()
    {
        var engine = new RuleEngine();
        engine.Allowlist.Add("image.ruliweb.com");
        Assert.False(engine.Evaluate("image.ruliweb.com").Blocked);
    }
}
