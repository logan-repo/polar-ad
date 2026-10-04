using PolarAd.Core.Blocklist;

namespace PolarAd.Tests;

public class HostsFileParserTests
{
    [Fact]
    public void Parses_standard_hosts_format()
    {
        var text = """
            # Title: test list
            0.0.0.0 ads.example.com
            0.0.0.0 tracker.example.net
            127.0.0.1 localhost
            0.0.0.0 0.0.0.0

            # a comment line
            0.0.0.0 another-ad.example.org # inline comment
            """;

        using var reader = new StringReader(text);
        var domains = HostsFileParser.ParseDomains(reader).ToList();

        Assert.Contains("ads.example.com", domains);
        Assert.Contains("tracker.example.net", domains);
        Assert.Contains("another-ad.example.org", domains);
        Assert.DoesNotContain("localhost", domains);
        Assert.DoesNotContain("0.0.0.0", domains);
    }

    [Fact]
    public void Ignores_non_hosts_lines()
    {
        var text = "not a hosts line\n192.168.1.1 router.local\n";
        using var reader = new StringReader(text);
        var domains = HostsFileParser.ParseDomains(reader).ToList();
        Assert.Empty(domains);
    }

    [Fact]
    public void Multiple_hostnames_on_one_line_are_all_captured()
    {
        var text = "0.0.0.0 ad1.example.com ad2.example.com\n";
        using var reader = new StringReader(text);
        var domains = HostsFileParser.ParseDomains(reader).ToList();
        Assert.Contains("ad1.example.com", domains);
        Assert.Contains("ad2.example.com", domains);
    }
}
