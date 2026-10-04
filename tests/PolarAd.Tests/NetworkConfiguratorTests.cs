using PolarAd.Core.Network;

namespace PolarAd.Tests;

/// <summary>
/// Only exercises the read-only path (enumerating adapters via WMI). Deliberately
/// never calls EnableLoopbackDns/RestoreOriginalDns here — those mutate the
/// machine's real DNS settings and must only run when a person explicitly turns
/// protection on/off in the app.
/// </summary>
public class NetworkConfiguratorTests
{
    [Fact]
    public void Can_enumerate_active_adapters_via_wmi()
    {
        var configurator = new NetworkConfigurator(Path.Combine(Path.GetTempPath(), "polarad-test-dns-backup.json"));
        var adapters = configurator.GetActiveAdapterConfigs();

        Assert.NotNull(adapters);
        // On any machine with networking enabled there is at least one IP-enabled adapter.
        Assert.True(adapters.Count >= 1);
    }
}
