using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Xunit;

namespace EquipmentLibrary.Api.Tests.Identity;

public sealed class AuthenticationTests
{
    private static readonly Uri Queue = new("/api/pickup-desk/queue", UriKind.Relative);

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("made-up", HttpStatusCode.Unauthorized)]
    [InlineData("demo-borrower-a", HttpStatusCode.Forbidden)]
    public async Task StaffQueueChecksTheIdentityBeforeOpeningTheDatabase(string? token, HttpStatusCode expected)
    {
        await using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host => host.UseSetting("Workers:Enabled", "false"));
        using HttpClient client = factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using HttpResponseMessage response = await client.GetAsync(Queue);
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task AProductionHostNeverAcceptsDemoStaffCredentials()
    {
        await using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host => host.UseEnvironment("Production")
                .UseSetting("Workers:Enabled", "false")
                .UseSetting("Authentication:Authority", "https://issuer.example.test")
                .UseSetting("Authentication:Audience", "equipment-library")
                .ConfigureServices(services => services.PostConfigure<JwtBearerOptions>(
                    JwtBearerDefaults.AuthenticationScheme,
                    options => options.Configuration = new OpenIdConnectConfiguration
                    {
                        Issuer = "https://issuer.example.test"
                    })));
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "demo-staff");

        IAuthenticationSchemeProvider schemes = factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        Assert.Null(await schemes.GetSchemeAsync("LocalLibrary"));
        using HttpResponseMessage response = await client.GetAsync(Queue);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
