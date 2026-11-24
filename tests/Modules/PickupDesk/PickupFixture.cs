using System.Security.Claims;
using System.Text.Encodings.Web;
using EquipmentLibrary.PickupDesk.Contracts;
using EquipmentLibrary.PickupDesk.Inbox;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace EquipmentLibrary.PickupDesk.Tests;

public sealed class PickupFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private NpgsqlDataSource _server = null!;
    private readonly string _databaseName = $"pickup_test_{Guid.NewGuid():N}";
    private readonly string _runtimeRole = $"pickup_role_{Guid.NewGuid():N}";
    private readonly string _password = Guid.NewGuid().ToString("N");
    private WebApplication _app = null!;
    private NpgsqlDataSource _admin = null!;
    private NpgsqlDataSource _runtime = null!;

    internal HttpClient Client { get; private set; } = null!;
    internal IPickupInbox Inbox => _app.Services.GetRequiredService<IPickupInbox>();
    internal ProcessPickupInbox Worker => _app.Services.GetRequiredService<ProcessPickupInbox>();

    public async Task InitializeAsync()
    {
        string? adminConnection = Environment.GetEnvironmentVariable("EQLIB_TEST_PG_ADMIN");
        if (string.IsNullOrWhiteSpace(adminConnection))
        {
            _postgres = new PostgreSqlBuilder("postgres:17.12-alpine3.23").Build();
            await _postgres.StartAsync();
            adminConnection = _postgres.GetConnectionString();
        }

        _server = NpgsqlDataSource.Create(adminConnection);
        await using (NpgsqlCommand createDatabase = _server.CreateCommand($"CREATE DATABASE {_databaseName}"))
        {
            await createDatabase.ExecuteNonQueryAsync();
        }

        var databaseConnection = new NpgsqlConnectionStringBuilder(adminConnection) { Database = _databaseName };
        _admin = NpgsqlDataSource.Create(databaseConnection.ConnectionString);
        await ExecuteAsync($"CREATE ROLE {_runtimeRole} LOGIN PASSWORD '{_password}'; CREATE SCHEMA pickup_desk; GRANT USAGE ON SCHEMA pickup_desk TO {_runtimeRole}");
        foreach (string file in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Migrations"), "*.sql").Order())
        {
            await ExecuteAsync(await File.ReadAllTextAsync(file));
        }

        await ExecuteAsync($"GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA pickup_desk TO {_runtimeRole}; GRANT USAGE ON ALL SEQUENCES IN SCHEMA pickup_desk TO {_runtimeRole}");
        databaseConnection.Username = _runtimeRole;
        databaseConnection.Password = _password;
        _runtime = NpgsqlDataSource.Create(databaseConnection.ConnectionString);

        WebApplicationBuilder builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { ApplicationName = typeof(PickupFixture).Assembly.GetName().Name });
        builder.Services.AddLogging();
        builder.Services.AddRouting();
        builder.WebHost.UseTestServer();
        builder.Services.AddKeyedSingleton("PickupDesk", _runtime);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestIdentity>("Test", null);
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy("Pickup.Manage", p => p.RequireClaim("permission", "Pickup.Manage"))
            .AddPolicy("Pickup.Operate", p => p.RequireClaim("permission", "Pickup.Operate"));
        builder.Services.AddPickupDesk(runWorker: false);
        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapPickupDesk();
        await _app.StartAsync();
        Client = _app.GetTestClient();
    }

    internal async Task ExecuteAsync(string sql)
    {
        await using NpgsqlCommand command = _admin.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    internal async Task<long> CountAsync(string sql)
    {
        await using NpgsqlCommand command = _admin.CreateCommand(sql);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (_app is not null) await _app.DisposeAsync();
        if (_runtime is not null) await _runtime.DisposeAsync();
        if (_admin is not null) await _admin.DisposeAsync();
        if (_server is not null)
        {
            await using NpgsqlCommand dropDatabase = _server.CreateCommand($"DROP DATABASE IF EXISTS {_databaseName} WITH (FORCE)");
            await dropDatabase.ExecuteNonQueryAsync();
            await using NpgsqlCommand dropRole = _server.CreateCommand($"DROP ROLE IF EXISTS {_runtimeRole}");
            await dropRole.ExecuteNonQueryAsync();
            await _server.DisposeAsync();
        }

        if (_postgres is not null) await _postgres.DisposeAsync();
    }

    private sealed class TestIdentity(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string? actor = Request.Headers["X-Test-Actor"].FirstOrDefault();
            if (actor is null)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, actor == "other" ? "20000000-0000-4000-8000-000000000002" : "20000000-0000-4000-8000-000000000001")
            };
            if (actor is "staff" or "operator" or "other") claims.Add(new("permission", "Pickup.Manage"));
            if (actor == "operator") claims.Add(new("permission", "Pickup.Operate"));

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
