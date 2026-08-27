using KaApi.Models;
using Microsoft.Extensions.Options;

namespace KaApi.Services;

public sealed class StartupOrderActivationService(
    IHttpClientFactory httpClientFactory,
    IOptions<StartupOrderActivationOptions> options,
    ILogger<StartupOrderActivationService> logger) : IHostedService
{
    private readonly StartupOrderActivationOptions _options = options.Value;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Startup order activation is disabled.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.Url))
        {
            throw new InvalidOperationException("Startup order activation URL is missing.");
        }

        logger.LogInformation(
            "Running startup order activation request. Url={Url}, TimeoutSeconds={TimeoutSeconds}",
            _options.Url,
            _options.TimeoutSeconds);

        var client = httpClientFactory.CreateClient(nameof(StartupOrderActivationService));
        client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds <= 0 ? 30 : _options.TimeoutSeconds);

        using var request = new HttpRequestMessage(HttpMethod.Get, _options.Url);
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var message =
                    $"Startup order activation failed with status {(int)response.StatusCode} ({response.ReasonPhrase}).";

                if (_options.FailStartupOnError)
                {
                    throw new InvalidOperationException(message);
                }

                logger.LogWarning(
                    "Startup order activation failed but application startup will continue. StatusCode={StatusCode}, ReasonPhrase={ReasonPhrase}, ResponseLength={ResponseLength}",
                    (int)response.StatusCode,
                    response.ReasonPhrase,
                    responseBody.Length);
                return;
            }

            logger.LogInformation(
                "Startup order activation completed successfully. StatusCode={StatusCode}, ResponseLength={ResponseLength}",
                (int)response.StatusCode,
                responseBody.Length);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            if (_options.FailStartupOnError)
            {
                throw;
            }

            logger.LogWarning(
                exception,
                "Startup order activation failed but application startup will continue. Url={Url}",
                _options.Url);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
