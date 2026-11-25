using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PrenotaConMe.Modules.Chatbot;
using PrenotaConMe.Modules.Notifications;
using PrenotaConMe.Modules.Notifications.Push;
using PrenotaConMe.Modules.Notifications.Telegram;
using Testcontainers.PostgreSql;

namespace PrenotaConMe.IntegrationTests;

/// <summary>
/// Boots the API against a real PostgreSQL:
/// - if PRENOTA_TEST_PG is set (e.g. "Host=localhost;Username=prenota;Password=prenota", role needs CREATEDB),
///   a throwaway database is created on that server (no Docker needed);
/// - otherwise a Testcontainers PostgreSQL container is started (requires Docker, used in CI).
/// Authentication is replaced by <see cref="TestAuthHandler"/>.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private static readonly string? LocalServer = Environment.GetEnvironmentVariable("PRENOTA_TEST_PG");

    private PostgreSqlContainer? _container;
    private string? _localDatabase;
    private string _connectionString = string.Empty;

    /// <summary>Uid whose test e-mail (uid@test.local) is configured as platform super-admin.</summary>
    public const string PlatformAdminUid = "platform-root";
    private const string PlatformAdminEmail = PlatformAdminUid + "@test.local";

    public const string TestPushPublicKey = "test-public-key";
    public const string PublicUrl = "https://prenota.test";

    /// <summary>The Telegram Bot API (replaced).</summary>
    public FakeTelegramApi Telegram { get; } = new();

    /// <summary>The chatbot's language model (replaced by a script the tests fill).</summary>
    public ScriptedChatModel ChatModel { get; } = new();

    /// <summary>E-mails sent by the app (SMTP is replaced).</summary>
    public CapturingEmailSender Emails { get; } = new();

    /// <summary>Push notifications sent by the app (the push services are replaced).</summary>
    public CapturingPushSender Pushes { get; } = new();

    public async Task InitializeAsync()
    {
        if (!string.IsNullOrWhiteSpace(LocalServer))
        {
            _localDatabase = $"prenota_test_{Guid.NewGuid():N}";
            await ExecuteOnServerAsync($"CREATE DATABASE \"{_localDatabase}\"");
            _connectionString = new NpgsqlConnectionStringBuilder(LocalServer) { Database = _localDatabase }.ConnectionString;
        }
        else
        {
            _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
            await _container.StartAsync();
            _connectionString = _container.GetConnectionString();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Database", _connectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Platform:AdminEmails:0", PlatformAdminEmail);
        builder.UseSetting("Push:PublicKey", TestPushPublicKey);
        builder.UseSetting("Push:PrivateKey", "test-private-key");
        builder.UseSetting("Telegram:Mode", "Webhook");
        // Every test class runs its own app (and Hangfire server) in parallel: keep each one's DB connections low.
        builder.UseSetting("Jobs:WorkerCount", "2");
        builder.UseSetting("Notifications:PublicUrl", PublicUrl);
        builder.UseSetting("OpenAI:ApiKey", "test-key");

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IEmailSender>(Emails);
            services.AddSingleton<IPushSender>(Pushes);
            services.AddSingleton<ITelegramApi>(Telegram);
            services.AddSingleton<IChatModel>(ChatModel);
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultScheme = TestAuthHandler.SchemeName;
                o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            });
        });
    }

    /// <summary>Client authenticated as <paramref name="uid"/> (and optionally scoped to a tenant slug).</summary>
    public HttpClient CreateUserClient(string uid, string? tenantSlug = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.Header, $"{uid}|{uid}@test.local|{uid}");
        if (tenantSlug is not null)
        {
            client.DefaultRequestHeaders.Add("X-Tenant", tenantSlug);
        }

        return client;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }

        if (_localDatabase is not null)
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteOnServerAsync($"DROP DATABASE IF EXISTS \"{_localDatabase}\" WITH (FORCE)");
        }
    }

    private static async Task ExecuteOnServerAsync(string sql)
    {
        var admin = new NpgsqlConnectionStringBuilder(LocalServer) { Database = "postgres", Pooling = false };
        await using var connection = new NpgsqlConnection(admin.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
