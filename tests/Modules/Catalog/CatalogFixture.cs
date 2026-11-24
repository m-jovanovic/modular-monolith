using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace EquipmentLibrary.Catalog.Tests;

public sealed class CatalogFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private WebApplication? application;

    public HttpClient Public { get; private set; } = null!;
    public HttpClient Staff { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        await using var admin = NpgsqlDataSource.Create(postgres.GetConnectionString());
        await using NpgsqlCommand bootstrap = admin.CreateCommand("""
            CREATE ROLE catalog_runtime LOGIN PASSWORD 'catalog_test_only';
            CREATE SCHEMA catalog;
            GRANT USAGE ON SCHEMA catalog TO catalog_runtime;
            ALTER DEFAULT PRIVILEGES IN SCHEMA catalog
                GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO catalog_runtime;
            """);
        await bootstrap.ExecuteNonQueryAsync();

        foreach (string path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Migrations"), "*.sql").Order(StringComparer.Ordinal))
        {
            await using NpgsqlCommand migrate = admin.CreateCommand(await File.ReadAllTextAsync(path));
            await migrate.ExecuteNonQueryAsync();
        }

        var runtime = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString())
        {
            Username = "catalog_runtime", Password = "catalog_test_only"
        };
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddKeyedSingleton("Catalog", (_, _) => NpgsqlDataSource.Create(runtime.ConnectionString));
        builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>("fixture", null);
        builder.Services.AddAuthorizationBuilder().AddPolicy("Catalog.Manage", p => p.RequireClaim("permission", "catalog.manage"));
        builder.Services.AddCatalog();
        application = builder.Build();
        application.UseAuthentication();
        application.UseAuthorization();
        application.MapCatalog();
        await application.StartAsync();
        Public = application.GetTestClient();
        Staff = application.GetTestClient();
        Staff.DefaultRequestHeaders.Add("X-Test-Identity", "cataloguer");
    }

    public async Task DisposeAsync()
    {
        Public?.Dispose();
        Staff?.Dispose();
        if (application is not null)
        {
            await application.DisposeAsync();
        }

        await postgres.DisposeAsync();
    }

    private sealed class FixtureAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string identity = Request.Headers["X-Test-Identity"].ToString();
            if (identity.Length == 0)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new ClaimsIdentity(Scheme.Name);
            claims.AddClaim(new Claim(ClaimTypes.NameIdentifier, identity));
            if (identity == "cataloguer")
            {
                claims.AddClaim(new Claim("permission", "catalog.manage"));
            }

            var ticket = new AuthenticationTicket(new ClaimsPrincipal(claims), Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
