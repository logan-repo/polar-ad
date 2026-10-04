using System.Text.RegularExpressions;

namespace PolarAd.Core.Blocklist;

public sealed class AdblockParseResult
{
    public HashSet<string> Blocked { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Excepted { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Parses the subset of AdBlock Plus / uBlock Origin filter syntax that maps onto
/// whole-domain DNS blocking: bare network rules of the form "||domain.tld^" (and
/// their exceptions "@@||domain.tld^") with NO trailing "$modifier".
///
/// Modifiers are deliberately a hard stop, not just path rules. A rule like
/// "||dcinside.com^$cookie=gaejuki_ad" means "strip one specific cookie on
/// dcinside.com" — it says nothing about blocking the domain at all. Other
/// modifiers ($script, $image, $third-party, $csp=, $removeparam=, ...) all scope
/// the rule to a request type, origin, or content rewrite that a DNS sinkhole has
/// no way to see or honor. A DNS proxy can only block or allow an entire domain,
/// so the only filter-list rules it can safely reproduce are the ones that already
/// mean exactly that: a bare "||domain^" with nothing after it. Treating a
/// modifier as "close enough, block the whole domain anyway" is how an unrelated
/// cookie-stripping rule ends up taking down an entire site (see git history / the
/// incident that prompted this comment).
/// </summary>
public static class AdblockRuleParser
{
    private static readonly Regex BlockPattern = new(
        @"^\|\|([a-z0-9]([a-z0-9\-\.]*[a-z0-9])?)\^$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static AdblockParseResult Parse(TextReader reader)
    {
        var result = new AdblockParseResult();

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;

            // Comments and metadata.
            if (trimmed.StartsWith('!') || trimmed.StartsWith('[')) continue;

            // Cosmetic / element-hiding rules — not something DNS blocking can do.
            if (trimmed.Contains("##") || trimmed.Contains("#@#") || trimmed.Contains("#?#")) continue;

            bool isException = trimmed.StartsWith("@@", StringComparison.Ordinal);
            var rule = isException ? trimmed[2..] : trimmed;

            var match = BlockPattern.Match(rule);
            if (!match.Success) continue;

            var domain = match.Groups[1].Value.ToLowerInvariant();
            if (!IsPlausibleDomain(domain)) continue;

            if (isException)
            {
                result.Excepted.Add(domain);
            }
            else
            {
                result.Blocked.Add(domain);
            }
        }

        return result;
    }

    private static bool IsPlausibleDomain(string domain)
    {
        return domain.Contains('.') && domain.Length <= 253;
    }
}
