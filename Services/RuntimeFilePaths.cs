using KaApi.Models;

namespace KaApi.Services;

public sealed class RuntimeFilePaths
{
    private readonly FileStorageOptions _options;

    public RuntimeFilePaths(FileStorageOptions options)
    {
        _options = options;
    }

    public DateTime StartedAt { get; } = DateTime.Now;

    public string RootPath => _options.RootPath;

    public string TraceDirectoryPath => Path.Combine(_options.RootPath, _options.TraceFolderName);

    public string TraceFilePath => GetTraceFilePath(StartedAt);

    public string PayloadRootPath => Path.Combine(_options.RootPath, _options.PayloadFolderName);

    public string GetPayloadZipPath(DateTime createdAt)
    {
        Directory.CreateDirectory(PayloadRootPath);
        return Path.Combine(PayloadRootPath, $"{createdAt:yyyy-MM-dd}.zip");
    }

    public string GetTraceFilePath(DateTime createdAt)
    {
        return Path.Combine(
            TraceDirectoryPath,
            $"{createdAt:yyyy-MM-dd}.log");
    }

    public string CreatePayloadEntryName(
        string directionPrefix,
        DateTime createdAt,
        string? requestNumber = null,
        string requestNumberSeparator = "-")
    {
        var requestNumberSuffix = string.IsNullOrWhiteSpace(requestNumber)
            ? string.Empty
            : $"{requestNumberSeparator}{SanitizeFileNamePart(requestNumber)}";

        return $"{createdAt:HHmmssfff}_{directionPrefix}{requestNumberSuffix}.xml";
    }

    public void EnsureTraceDirectory()
    {
        Directory.CreateDirectory(TraceDirectoryPath);
    }

    private static string SanitizeFileNamePart(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Trim()
            .Select(character => invalidChars.Contains(character) || char.IsWhiteSpace(character) ? '_' : character)
            .ToArray());

        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }
}
