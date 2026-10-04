using PolarAd.Core.Rules;

namespace PolarAd.Core.Blocklist;

public sealed class BlocklistUpdateResult
{
    public bool Success { get; init; }
    public int DomainCount { get; init; }
    public List<string> Errors { get; init; } = new();
}

/// <summary>
/// Downloads one or more public hosts-format blocklists, merges them, and persists
/// the merged domain set to a local cache file so the app works offline after first update.
/// </summary>
public sealed class BlocklistUpdater
{
    private readonly HttpClient _httpClient;
    private readonly string _cacheFilePath;

    public BlocklistUpdater(HttpClient httpClient, string cacheFilePath)
    {
        _httpClient = httpClient;
        _cacheFilePath = cacheFilePath;
    }

    public async Task<BlocklistUpdateResult> UpdateAsync(IEnumerable<string> sourceUrls, DomainSet target, CancellationToken ct = default)
    {
        var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exceptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();

        foreach (var url in sourceUrls)
        {
            try
            {
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                var text = await response.Content.ReadAsStringAsync(ct);

                // Each source may be either hosts-format or AdBlock Plus/uBlock
                // filter syntax. Both parsers simply find nothing on lines that
                // don't match their own format, so running both is safe and
                // avoids needing to sniff/guess the format per source.
                using (var hostsReader = new StringReader(text))
                {
                    foreach (var domain in HostsFileParser.ParseDomains(hostsReader))
                    {
                        merged.Add(domain);
                    }
                }

                using (var adblockReader = new StringReader(text))
                {
                    var adblockResult = AdblockRuleParser.Parse(adblockReader);
                    foreach (var domain in adblockResult.Blocked)
                    {
                        merged.Add(domain);
                    }
                    foreach (var domain in adblockResult.Excepted)
                    {
                        exceptions.Add(domain);
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{url}: {ex.Message}");
            }
        }

        merged.ExceptWith(exceptions);

        if (merged.Count == 0)
        {
            return new BlocklistUpdateResult { Success = false, DomainCount = 0, Errors = errors };
        }

        target.ReplaceAll(merged);
        await SaveCacheAsync(merged, ct);

        return new BlocklistUpdateResult { Success = true, DomainCount = merged.Count, Errors = errors };
    }

    public async Task<bool> LoadFromCacheAsync(DomainSet target, CancellationToken ct = default)
    {
        if (!File.Exists(_cacheFilePath))
        {
            return false;
        }

        var lines = await File.ReadAllLinesAsync(_cacheFilePath, ct);
        target.ReplaceAll(lines);
        return target.Count > 0;
    }

    private async Task SaveCacheAsync(IEnumerable<string> domains, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_cacheFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        await File.WriteAllLinesAsync(_cacheFilePath, domains, ct);
    }
}
