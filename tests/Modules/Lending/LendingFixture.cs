using System.Security.Claims;
using System.Text.Encodings.Web;
using EquipmentLibrary.Catalog.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace EquipmentLibrary.Lending.Tests;

public sealed class LendingFixture : IAsyncLifetime
{
    public static readonly Guid CatalogItemId = Guid.Parse("f22a8917-a47f-48bf-91f3-01f566c2d78f");
    private static readonly string[] Permissions = ["Lending.Enroll", "Pickup.Manage", "Staff", "Borrower"];
    private readonly string _id = Guid.NewGuid().ToString("N");
    private PostgreSqlContainer? _postgres;
    private NpgsqlDataSource? _cluster;
    private WebApplication _app = null!;
    internal ScenarioClock Clock { get; } = new();
    public NpgsqlDataSource Admin { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        string? external = Environment.GetEnvironmentVariable("EQLIB_TEST_PG_ADMIN");
        string connectionString;
        if (string.IsNullOrWhiteSpace(external))
        {
            _postgres = new PostgreSqlBuilder("postgres:17.12-alpine3.23").Build();
            await _postgres.StartAsync();
            connectionString = _postgres.GetConnectionString();
        }
        else
        {
            _cluster = NpgsqlDataSource.Create(external);
            await using NpgsqlCommand create = _cluster.CreateCommand($"CREATE DATABASE lending_test_{_id}");
            await create.ExecuteNonQueryAsync();
            connectionString = new NpgsqlConnectionStringBuilder(external) { Database = $"lending_test_{_id}" }.ConnectionString;
        }

        Admin = NpgsqlDataSource.Create(connectionString);
        string role = $"lending_test_{_id}";
        string password = Guid.NewGuid().ToString("N");
        await using NpgsqlCommand setup = Admin.CreateCommand($"""
            CREATE SCHEMA lending;
            CREATE ROLE {role} LOGIN PASSWORD '{password}';
            GRANT USAGE ON SCHEMA lending TO {role};
            """);
        await setup.ExecuteNonQueryAsync();
        foreach (string path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Migrations"), "*.sql").Order(StringComparer.Ordinal))
        {
            await using NpgsqlCommand migration = Admin.CreateCommand(await File.ReadAllTextAsync(path));
            await migration.ExecuteNonQueryAsync();
        }

        await using NpgsqlCommand grants = Admin.CreateCommand(
            $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA lending TO {role}");
        await grants.ExecuteNonQueryAsync();

        var runtime = new NpgsqlConnectionStringBuilder(connectionString) { Username = role, Password = password };
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(["--hostBuilder:reloadConfigOnChange=false"]);
        builder.WebHost.UseTestServer();
        builder.Services.AddKeyedSingleton("Lending", (_, _) => NpgsqlDataSource.Create(runtime.ConnectionString));
        builder.Services.AddSingleton<TimeProvider>(Clock);
        builder.Services.AddSingleton<ICatalogLookup, KnownCatalog>();
        builder.Services.AddLending(runExpiryWorker: false);
        builder.Services.AddAuthentication("Fixture").AddScheme<AuthenticationSchemeOptions, FixtureIdentity>("Fixture", _ => { });
        builder.Services.AddAuthorization(options =>
        {
            foreach (string permission in Permissions)
            {
                options.AddPolicy(permission, policy => policy.RequireClaim("permission", permission));
            }
        });
        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapLending();
        await _app.StartAsync();
    }

    public HttpClient Client(string persona)
    {
        HttpClient client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Fixture-Identity", persona);
        return client;
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        if (Admin is not null)
        {
            await Admin.DisposeAsync();
        }

        if (_cluster is not null)
        {
            await using NpgsqlCommand dropDatabase = _cluster.CreateCommand($"DROP DATABASE IF EXISTS lending_test_{_id} WITH (FORCE)");
            await dropDatabase.ExecuteNonQueryAsync();
            await using NpgsqlCommand dropRole = _cluster.CreateCommand($"DROP ROLE IF EXISTS lending_test_{_id}");
            await dropRole.ExecuteNonQueryAsync();
            await _cluster.DisposeAsync();
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    private sealed class KnownCatalog : ICatalogLookup
    {
        public Task<CatalogItemDescription?> GetItemAsync(Guid itemId, CancellationToken cancellationToken) =>
            Task.FromResult(itemId == CatalogItemId ? new CatalogItemDescription(itemId, "Cordless drill", "18 V") : null);
    }

    private sealed class FixtureIdentity(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string? persona = Request.Headers["X-Fixture-Identity"].FirstOrDefault();
            if (persona is not ("a" or "b" or "staff"))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            string subject = persona switch
            {
                "a" => "77b45367-0535-4fb5-8f53-58308ea6fe6d",
                "b" => "07ac1363-f8ff-4880-b819-64132f07c5f2",
                _ => "ad85e18d-4c59-45f4-b32e-a8d990c7cf90"
            };
            string[] permissions = persona == "staff" ? ["Staff", "Lending.Enroll", "Pickup.Manage"] : ["Borrower"];
            var claims = permissions.Select(permission => new Claim("permission", permission)).ToList();
            claims.Add(new Claim(ClaimTypes.NameIdentifier, subject));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
