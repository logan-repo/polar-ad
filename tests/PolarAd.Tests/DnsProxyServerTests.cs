using System.Net;
using System.Net.Sockets;
using PolarAd.Core.Dns;
using PolarAd.Core.Rules;

namespace PolarAd.Tests;

/// <summary>
/// End-to-end tests against a real DnsProxyServer bound to an unprivileged loopback
/// port (never touches the machine's actual DNS settings or port 53).
/// </summary>
public class DnsProxyServerTests
{
    private static int NextTestPort() => Random.Shared.Next(20000, 40000);

    [Fact]
    public async Task Blocked_domain_returns_nxdomain()
    {
        var ruleEngine = new RuleEngine();
        ruleEngine.PublicBlocklist.Add("ads.example.com");

        var port = NextTestPort();
        var server = new DnsProxyServer(ruleEngine, new IPEndPoint(IPAddress.Parse("1.1.1.1"), 53), port);
        server.Start();

        try
        {
            using var client = new UdpClient();
            client.Client.ReceiveTimeout = 5000;
            var query = DnsTestHelper.BuildQuery(42, "ads.example.com");
            await client.SendAsync(query, query.Length, new IPEndPoint(IPAddress.Loopback, port));

            var response = await ReceiveWithTimeoutAsync(client, TimeSpan.FromSeconds(5));
            Assert.NotNull(response);
            Assert.Equal(0x83, response![3]); // RCODE = NXDOMAIN
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task Allowed_domain_is_forwarded_to_upstream()
    {
        var ruleEngine = new RuleEngine(); // nothing blocked
        var port = NextTestPort();
        var server = new DnsProxyServer(ruleEngine, new IPEndPoint(IPAddress.Parse("1.1.1.1"), 53), port);
        server.Start();

        try
        {
            using var client = new UdpClient();
            client.Client.ReceiveTimeout = 5000;
            var query = DnsTestHelper.BuildQuery(99, "cloudflare.com");
            await client.SendAsync(query, query.Length, new IPEndPoint(IPAddress.Loopback, port));

            var response = await ReceiveWithTimeoutAsync(client, TimeSpan.FromSeconds(5));
            Assert.NotNull(response);

            int rcode = response![3] & 0x0F;
            Assert.Equal(0, rcode); // NOERROR — a real answer came back from upstream
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task User_allowlist_overrides_public_blocklist_end_to_end()
    {
        var ruleEngine = new RuleEngine();
        ruleEngine.PublicBlocklist.Add("cloudflare.com");
        ruleEngine.Allowlist.Add("cloudflare.com");

        var port = NextTestPort();
        var server = new DnsProxyServer(ruleEngine, new IPEndPoint(IPAddress.Parse("1.1.1.1"), 53), port);
        server.Start();

        try
        {
            using var client = new UdpClient();
            client.Client.ReceiveTimeout = 5000;
            var query = DnsTestHelper.BuildQuery(7, "cloudflare.com");
            await client.SendAsync(query, query.Length, new IPEndPoint(IPAddress.Loopback, port));

            var response = await ReceiveWithTimeoutAsync(client, TimeSpan.FromSeconds(5));
            Assert.NotNull(response);
            int rcode = response![3] & 0x0F;
            Assert.Equal(0, rcode); // forwarded, not blocked
        }
        finally
        {
            await server.StopAsync();
        }
    }

    private static async Task<byte[]?> ReceiveWithTimeoutAsync(UdpClient client, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            var result = await client.ReceiveAsync(cts.Token);
            return result.Buffer;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}
