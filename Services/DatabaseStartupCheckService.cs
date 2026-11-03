using KaApi.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace KaApi.Services;

public class DatabaseStartupCheckService(
    IConfiguration configuration,
    IOptions<DatabaseStartupRetryOptions> options,
    ILogger<DatabaseStartupCheckService> logger) : IHostedService
{
    private readonly string _connectionString = configuration.GetConnectionString("KmDatabase")
        ?? throw new InvalidOperationException("Connection string 'KmDatabase' is missing.");
    private readonly DatabaseStartupRetryOptions _options = options.Value;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var connectionSettings = new SqlConnectionStringBuilder(_connectionString);
        logger.LogInformation(
            "Validating SQL connection during application startup. DatabaseServer={DatabaseServer}, DatabaseName={DatabaseName}, RetryIntervalSeconds={RetryIntervalSeconds}",
            connectionSettings.DataSource,
            connectionSettings.InitialCatalog,
            RetryIntervalSeconds);

        var attempt = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;

            try
            {
                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);

                await using var command = new SqlCommand("SELECT 1", connection);
                await command.ExecuteScalarAsync(cancellationToken);

                logger.LogInformation(
                    "SQL connection validated successfully. IsConnected={IsConnected}, DatabaseServer={DatabaseServer}, DatabaseName={DatabaseName}, Attempt={Attempt}",
                    connection.State == System.Data.ConnectionState.Open,
                    connectionSettings.DataSource,
                    connectionSettings.InitialCatalog,
                    attempt);
                return;
            }
            catch (SqlException exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    exception,
                    "SQL connection is unavailable. DatabaseServer={DatabaseServer}, DatabaseName={DatabaseName}, Attempt={Attempt}. Retrying in {RetryIntervalSeconds} seconds.",
                    connectionSettings.DataSource,
                    connectionSettings.InitialCatalog,
                    attempt,
                    RetryIntervalSeconds);

                await Task.Delay(TimeSpan.FromSeconds(RetryIntervalSeconds), cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private int RetryIntervalSeconds => Math.Max(1, _options.RetryIntervalSeconds);
}
