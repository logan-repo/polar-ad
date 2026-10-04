using PolarAd.Core.Blocklist;

namespace PolarAd.Tests;

public class AdblockRuleParserTests
{
    [Fact]
    public void Parses_bare_domain_block_rules()
    {
        var text = """
            ! Title: test filter list
            ||ads.example.com^
            """;

        using var reader = new StringReader(text);
        var result = AdblockRuleParser.Parse(reader);

        Assert.Contains("ads.example.com", result.Blocked);
    }

    [Fact]
    public void Rules_with_any_modifier_are_never_treated_as_a_full_domain_block()
    {
        // Regression test for a real incident: the AdGuard Korean filter list
        // contains "||dcinside.com^$cookie=gaejuki_ad" — a rule that strips one
        // cookie, not a site block. An earlier version of this parser treated any
        // "$..." suffix as "still block the whole domain", which took down all of
        // dcinside.com. A DNS sinkhole can only block/allow whole domains, so any
        // modifier (which scopes the rule to a request type, cookie, origin, etc.)
        // must cause the rule to be skipped entirely rather than approximated.
        var text = """
            ||dcinside.com^$cookie=gaejuki_ad
            ||tracker.example.net^$third-party
            ||ads.example.com^$script,domain=example.com
            ||safe-to-block.example.com^
            """;

        using var reader = new StringReader(text);
        var result = AdblockRuleParser.Parse(reader);

        Assert.DoesNotContain("dcinside.com", result.Blocked);
        Assert.DoesNotContain("tracker.example.net", result.Blocked);
        Assert.DoesNotContain("ads.example.com", result.Blocked);
        Assert.Contains("safe-to-block.example.com", result.Blocked);
    }

    [Fact]
    public void Parses_exception_rules_separately()
    {
        var text = "@@||safe.example.com^\n||safe.example.com^\n";
        using var reader = new StringReader(text);
        var result = AdblockRuleParser.Parse(reader);

        Assert.Contains("safe.example.com", result.Excepted);
    }

    [Fact]
    public void Ignores_cosmetic_rules()
    {
        var text = "example.com##.ad-banner\nexample.com#@#.ad-banner\n";
        using var reader = new StringReader(text);
        var result = AdblockRuleParser.Parse(reader);

        Assert.Empty(result.Blocked);
        Assert.Empty(result.Excepted);
    }

    [Fact]
    public void Ignores_path_scoped_rules_it_cannot_enforce_via_dns()
    {
        var text = "||example.com/ads/banner.js^\n";
        using var reader = new StringReader(text);
        var result = AdblockRuleParser.Parse(reader);

        Assert.Empty(result.Blocked);
    }

    [Fact]
    public void Ignores_comments_and_blank_lines()
    {
        var text = "! comment\n\n[Adblock Plus 2.0]\n||ads.example.com^\n";
        using var reader = new StringReader(text);
        var result = AdblockRuleParser.Parse(reader);

        Assert.Single(result.Blocked);
        Assert.Contains("ads.example.com", result.Blocked);
    }
}
