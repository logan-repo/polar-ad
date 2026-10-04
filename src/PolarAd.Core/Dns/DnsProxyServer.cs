using System.Net;
using System.Net.Sockets;
using PolarAd.Core.Diagnostics;
using PolarAd.Core.Models;
using PolarAd.Core.Rules;

namespace PolarAd.Core.Dns;

public sealed class DnsQueryObserved
{
    public required string Domain { get; init; }
    public required BlockDecision Decision { get; init; }
    public required string MatchedRule { get; init; }
    public required string AppName { get; init; }
    public required int ProcessId { get; init; }
}

/// <summary>
/// A local UDP DNS server. Domains that match a block rule get an NXDOMAIN reply
/// synthesized locally; everything else is forwarded verbatim to an upstream resolver
/// and the raw response is relayed back, so we never need to implement full record
/// encoding for real answers.
/// </summary>
public sealed class DnsProxyServer : IAsyncDisposable
{
    private readonly RuleEngine _ruleEngine;
    private readonly IPEndPoint _upstream;
    private readonly ProcessPortLookup _processLookup;
    private UdpClient? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public event Action<DnsQueryObserved>? QueryObserved;

    public int ListenPort { get; }

    public DnsProxyServer(RuleEngine ruleEngine, IPEndPoint upstream, int listenPort = 53)
    {
        _ruleEngine = ruleEngine;
        _upstream = upstream;
        ListenPort = listenPort;
        _processLookup = new ProcessPortLookup();
    }

    public void Start()
    {
        if (_listener != null)
        {
            return;
        }

        _listener = new UdpClient(AddressFamily.InterNetwork);
        _listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Client.Bind(new IPEndPoint(IPAddress.Loopback, ListenPort));

        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => RunLoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        if (_cts == null)
        {
            return;
        }

        _cts.Cancel();
        _listener?.Close();
        try
        {
            if (_loopTask != null)
            {
                await _loopTask;
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }

        _listener = null;
        _cts = null;
        _loopTask = null;
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        var listener = _listener!;
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await listener.ReceiveAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                continue;
            }

            _ = HandleQueryAsync(result, ct);
        }
    }

    private async Task HandleQueryAsync(UdpReceiveResult result, CancellationToken ct)
    {
        try
        {
            var domain = DnsMessageUtils.TryReadQuestionName(result.Buffer);
            if (domain == null)
            {
                return;
            }

            var decision = _ruleEngine.Evaluate(domain);
            var (appName, pid) = _processLookup.ResolveOwner(result.RemoteEndPoint.Port);

            QueryObserved?.Invoke(new DnsQueryObserved
            {
                Domain = domain,
                Decision = decision.Decision,
                MatchedRule = decision.MatchedRule,
                AppName = appName,
                ProcessId = pid,
            });

            if (decision.Blocked)
            {
                var nx = DnsMessageUtils.BuildNxDomainResponse(result.Buffer);
                await _listener!.SendAsync(nx, nx.Length, result.RemoteEndPoint);
            }
            else
            {
                await ForwardAsync(result, ct);
            }
        }
        catch
        {
            // Never let a single malformed/odd query take down the listen loop.
        }
    }

    private async Task ForwardAsync(UdpReceiveResult result, CancellationToken ct)
    {
        using var upstreamClient = new UdpClient(AddressFamily.InterNetwork);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));

        try
        {
            await upstreamClient.SendAsync(result.Buffer, result.Buffer.Length, _upstream);
            var response = await upstreamClient.ReceiveAsync(timeoutCts.Token);
            await _listener!.SendAsync(response.Buffer, response.Buffer.Length, result.RemoteEndPoint);
        }
        catch (OperationCanceledException)
        {
            // Upstream timed out; the client's own resolver will retry.
        }
        catch (SocketException)
        {
            // Upstream unreachable; same as above.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
