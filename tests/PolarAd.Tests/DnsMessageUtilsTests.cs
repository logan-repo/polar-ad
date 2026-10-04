using PolarAd.Core.Dns;

namespace PolarAd.Tests;

public class DnsMessageUtilsTests
{
    [Fact]
    public void Reads_question_name_from_query()
    {
        var query = DnsTestHelper.BuildQuery(1234, "ads.example.com");
        var name = DnsMessageUtils.TryReadQuestionName(query);
        Assert.Equal("ads.example.com", name);
    }

    [Fact]
    public void Builds_nxdomain_response_preserving_id_and_question()
    {
        var query = DnsTestHelper.BuildQuery(0xABCD, "blocked.example.com");
        var response = DnsMessageUtils.BuildNxDomainResponse(query);

        // ID preserved
        Assert.Equal(query[0], response[0]);
        Assert.Equal(query[1], response[1]);

        // RCODE = 3 (NXDOMAIN) in low nibble of byte 3, QR bit set in byte 2
        Assert.Equal(0x81, response[2]);
        Assert.Equal(0x83, response[3]);

        // ANCOUNT/NSCOUNT/ARCOUNT all zero
        Assert.Equal(0, response[6] << 8 | response[7]);
        Assert.Equal(0, response[8] << 8 | response[9]);
        Assert.Equal(0, response[10] << 8 | response[11]);

        var echoedName = DnsMessageUtils.TryReadQuestionName(response);
        Assert.Equal("blocked.example.com", echoedName);
    }
}
