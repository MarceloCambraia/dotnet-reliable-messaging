using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<ContainersFixture>
{
    public const string Name = "integration";
}

/// <summary>
/// One Postgres and one RabbitMQ container shared by the whole test run. Tests isolate
/// themselves with unique database, exchange and queue names instead of restarting containers.
/// </summary>
public sealed class ContainersFixture : IAsyncLifetime
{
    public const string PostgresImage = "postgres:17-alpine";
    public const string RabbitMqImage = "rabbitmq:4.3-management-alpine";

    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder(PostgresImage).Build();

    public RabbitMqContainer RabbitMq { get; } = new RabbitMqBuilder(RabbitMqImage).Build();

    public string RabbitMqUri => RabbitMq.GetConnectionString();

    /// <summary>Connection string to a database that does not exist yet (EF migrations create it).</summary>
    public string NewDatabase(string prefix) =>
        new NpgsqlConnectionStringBuilder(Postgres.GetConnectionString()) { Database = TestNames.Unique(prefix).Replace('.', '_') }
            .ConnectionString;

    public Task InitializeAsync() => Task.WhenAll(Postgres.StartAsync(), RabbitMq.StartAsync());

    public async Task DisposeAsync()
    {
        await Postgres.DisposeAsync();
        await RabbitMq.DisposeAsync();
    }
}
