using System.Net;
using Hikari.WindowsClient.Core.Network;
using Hikari.WindowsClient.Core.Storage;

namespace Hikari.WindowsClient.Tests;

/// <summary>Hits the hosted sync-server. Run with <c>--filter Category!=Integration</c> offline.</summary>
[Trait("Category", "Integration")]
public class ServerIntegrationTests
{
    private readonly ApiClient _api = new(new AuthRepository());

    [Fact]
    public async Task HostedServerIsReachableAndServesTheContentApi()
    {
        var status = await _api.PingAsync(SettingsRepository.DefaultServerDomain);

        Assert.NotEqual(HttpStatusCode.NotFound, status);
        Assert.True((int)status < 500, $"server answered {(int)status}");
    }

    [Fact]
    public async Task ContentApiRejectsAnonymousRequests()
    {
        var status = await _api.PingAsync(SettingsRepository.DefaultServerDomain);

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    [Fact]
    public async Task UnreachableServerFailsTheProbe()
    {
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => _api.PingAsync("127.0.0.1:1"));
    }
}
