namespace PolarAd.Core.Rules;

/// <summary>
/// Case-insensitive set of domains supporting "exact or subdomain" matching,
/// e.g. adding "doubleclick.net" also matches "ads.doubleclick.net".
/// </summary>
public sealed class DomainSet
{
    private readonly HashSet<string> _domains = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _domains.Count;

    public void Add(string domain)
    {
        var normalized = Normalize(domain);
        if (normalized.Length > 0)
        {
            _domains.Add(normalized);
        }
    }

    public bool Remove(string domain) => _domains.Remove(Normalize(domain));

    public void Clear() => _domains.Clear();

    public IReadOnlyCollection<string> All() => _domains;

    public void ReplaceAll(IEnumerable<string> domains)
    {
        _domains.Clear();
        foreach (var d in domains)
        {
            Add(d);
        }
    }

    /// <summary>
    /// Returns the matching rule entry (the blocked parent/exact domain) or null if no match.
    /// </summary>
    public string? FindMatch(string queryDomain)
    {
        var normalized = Normalize(queryDomain);
        if (normalized.Length == 0) return null;

        if (_domains.Contains(normalized))
        {
            return normalized;
        }

        var labels = normalized.Split('.');
        for (int i = 1; i < labels.Length - 1; i++)
        {
            var parent = string.Join('.', labels[i..]);
            if (_domains.Contains(parent))
            {
                return parent;
            }
        }

        return null;
    }

    public bool Contains(string queryDomain) => FindMatch(queryDomain) != null;

    private static string Normalize(string domain)
    {
        domain = domain.Trim().TrimEnd('.');
        if (domain.StartsWith("*.", StringComparison.Ordinal))
        {
            domain = domain[2..];
        }
        return domain.ToLowerInvariant();
    }
}
