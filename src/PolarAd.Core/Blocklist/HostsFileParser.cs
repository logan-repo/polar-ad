namespace PolarAd.Core.Blocklist;

/// <summary>
/// Parses a "hosts" file formatted blocklist (e.g. StevenBlack/hosts) into a flat
/// set of blocked domains. Lines look like "0.0.0.0 ads.example.com".
/// </summary>
public static class HostsFileParser
{
    private static readonly HashSet<string> IgnoredHostnames = new(StringComparer.OrdinalIgnoreCase)
    {
        "localhost", "localhost.localdomain", "local", "broadcasthost",
        "ip6-localhost", "ip6-loopback", "ip6-localnet", "ip6-mcastprefix",
        "ip6-allnodes", "ip6-allrouters", "ip6-allhosts", "0.0.0.0",
    };

    public static IEnumerable<string> ParseDomains(TextReader reader)
    {
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var hashIdx = trimmed.IndexOf('#');
            if (hashIdx >= 0)
            {
                trimmed = trimmed[..hashIdx].Trim();
            }

            var parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            // Expect "<ip> <domain>" — only accept the two well-known sinkhole IPs.
            if (parts[0] != "0.0.0.0" && parts[0] != "127.0.0.1")
            {
                continue;
            }

            for (int i = 1; i < parts.Length; i++)
            {
                var domain = parts[i].Trim().ToLowerInvariant();
                if (domain.Length == 0 || IgnoredHostnames.Contains(domain))
                {
                    continue;
                }
                if (!IsPlausibleDomain(domain))
                {
                    continue;
                }
                yield return domain;
            }
        }
    }

    private static bool IsPlausibleDomain(string domain)
    {
        return domain.Contains('.') && !domain.Contains(' ') && domain.Length <= 253;
    }
}
