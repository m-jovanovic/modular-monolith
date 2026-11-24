using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EquipmentLibrary.Api.Tests;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task HealthReturnsHealthyFromTheRunningHost()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
