namespace KaApi.Models;

public class DatabaseStartupRetryOptions
{
    public const string SectionName = "DatabaseStartupRetry";

    public int RetryIntervalSeconds { get; set; } = 10;
}
