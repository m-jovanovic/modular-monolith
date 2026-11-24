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
    private readonly string databaseName = $"catalog_test_{Guid.NewGuid():N}";
    private readonly string runtimeRole = $"catalog_test_role_{Guid.NewGuid():N}";
    private readonly string password = Guid.NewGuid().ToString("N");
    private PostgreSqlContainer? postgres;
    private NpgsqlDataSource? control;
    private WebApplication? application;

    public HttpClient Public { get; private set; } = null!;
    public HttpClient Staff { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        string? server = Environment.GetEnvironmentVariable("EQLIB_TEST_PG_ADMIN");
        if (string.IsNullOrWhiteSpace(server))
        {
            postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
            using var startup = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await postgres.StartAsync(startup.Token);
            server = postgres.GetConnectionString();
        }

        // A separate database and runtime role keep parallel test runs isolated, including on a shared development server.
        control = NpgsqlDataSource.Create(server);
        await using (NpgsqlCommand createDatabase = control.CreateCommand($"CREATE DATABASE {databaseName}"))
        {
            await createDatabase.ExecuteNonQueryAsync();
        }

        var settings = new NpgsqlConnectionStringBuilder(server) { Database = databaseName };
        await using var admin = NpgsqlDataSource.Create(settings.ConnectionString);
        await using NpgsqlCommand bootstrap = admin.CreateCommand($"""
            CREATE ROLE {runtimeRole} LOGIN PASSWORD '{password}';
            CREATE SCHEMA catalog;
            GRANT USAGE ON SCHEMA catalog TO {runtimeRole};
            ALTER DEFAULT PRIVILEGES IN SCHEMA catalog
                GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {runtimeRole};
            """);
        await bootstrap.ExecuteNonQueryAsync();

        foreach (string path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Migrations"), "*.sql").Order(StringComparer.Ordinal))
        {
            await using NpgsqlCommand migrate = admin.CreateCommand(await File.ReadAllTextAsync(path));
            await migrate.ExecuteNonQueryAsync();
        }

        var runtime = new NpgsqlConnectionStringBuilder(settings.ConnectionString)
        {
            Username = runtimeRole,
            Password = password
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

        if (control is not null)
        {
            await using NpgsqlCommand dropDatabase = control.CreateCommand($"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE)");
            await dropDatabase.ExecuteNonQueryAsync();
            await using NpgsqlCommand dropRole = control.CreateCommand($"DROP ROLE IF EXISTS {runtimeRole}");
            await dropRole.ExecuteNonQueryAsync();
            await control.DisposeAsync();
        }

        if (postgres is not null)
        {
            await postgres.DisposeAsync();
        }
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

[CollectionDefinition(nameof(CatalogFixture))]
public sealed class CatalogDatabaseTests : ICollectionFixture<CatalogFixture>;
