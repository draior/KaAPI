using Microsoft.Data.SqlClient;

namespace KaApi.Services;

public class DatabaseStartupCheckService(
    IConfiguration configuration,
    ILogger<DatabaseStartupCheckService> logger) : IHostedService
{
    private readonly string _connectionString = configuration.GetConnectionString("KmDatabase")
        ?? throw new InvalidOperationException("Connection string 'KmDatabase' is missing.");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var connectionSettings = new SqlConnectionStringBuilder(_connectionString);
        logger.LogInformation(
            "Validating SQL connection during application startup. DatabaseServer={DatabaseServer}, DatabaseName={DatabaseName}",
            connectionSettings.DataSource,
            connectionSettings.InitialCatalog);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand("SELECT 1", connection);
        await command.ExecuteScalarAsync(cancellationToken);

        logger.LogInformation(
            "SQL connection validated successfully. IsConnected={IsConnected}, DatabaseServer={DatabaseServer}, DatabaseName={DatabaseName}",
            connection.State == System.Data.ConnectionState.Open,
            connectionSettings.DataSource,
            connectionSettings.InitialCatalog);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
