using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace KaApi.Services;

public sealed class PayloadArchiveService(
    RuntimeFilePaths runtimeFilePaths,
    ILogger<PayloadArchiveService> logger) : IPayloadArchiveService
{
    private readonly RuntimeFilePaths _runtimeFilePaths = runtimeFilePaths;
    private readonly ILogger<PayloadArchiveService> _logger = logger;
    private readonly SemaphoreSlim _archiveLock = new(1, 1);

    public async Task<string?> SaveIncomingAsync(HttpContext httpContext, string payload, CancellationToken cancellationToken)
    {
        var requestNumber = httpContext.Items.TryGetValue(RequestResponseArchiveMiddleware.RequestNumberItemKey, out var value)
            ? value as string
            : null;

        return await SaveAsync(httpContext, payload, "I", requestNumber, "_", cancellationToken);
    }

    public async Task<string?> SaveOutgoingAsync(HttpContext httpContext, string payload, CancellationToken cancellationToken)
    {
        var requestNumber = httpContext.Items.TryGetValue(RequestResponseArchiveMiddleware.RequestNumberItemKey, out var value)
            ? value as string
            : null;

        return await SaveAsync(httpContext, payload, "O", requestNumber, "_", cancellationToken);
    }

    private async Task<string?> SaveAsync(
        HttpContext httpContext,
        string payload,
        string directionPrefix,
        string? requestNumber,
        string requestNumberSeparator,
        CancellationToken cancellationToken)
    {
        var createdAt = DateTime.Now;
        var zipPath = _runtimeFilePaths.GetPayloadZipPath(createdAt);
        var entryName = _runtimeFilePaths.CreatePayloadEntryName(
            directionPrefix,
            createdAt,
            requestNumber,
            requestNumberSeparator);

        try
        {
            var archiveContent = BuildArchiveContent(
                httpContext,
                payload ?? string.Empty,
                directionPrefix,
                requestNumber,
                createdAt);

            await _archiveLock.WaitAsync(cancellationToken);
            try
            {
                using var zipStream = new FileStream(
                    zipPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.ReadWrite);
                using var archive = new ZipArchive(zipStream, ZipArchiveMode.Update);
                archive.GetEntry(entryName)?.Delete();
                var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);

                await using var entryStream = entry.Open();
                await using var writer = new StreamWriter(entryStream, new UTF8Encoding(false));
                await writer.WriteAsync(archiveContent.AsMemory(), cancellationToken);
                await writer.FlushAsync(cancellationToken);
            }
            finally
            {
                _archiveLock.Release();
            }

            _logger.LogInformation(
                "Saved {Direction} payload archive. Method={Method}, Path={Path}, RequestNumber={RequestNumber}, ZipPath={ZipPath}, EntryName={EntryName}",
                directionPrefix,
                httpContext.Request.Method,
                httpContext.Request.Path,
                requestNumber,
                zipPath,
                entryName);
            return $"{zipPath}|{entryName}";
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to save {Direction} payload archive. Method={Method}, Path={Path}",
                directionPrefix,
                httpContext.Request.Method,
                httpContext.Request.Path);
            return null;
        }
    }

    private static string BuildArchiveContent(
        HttpContext httpContext,
        string payload,
        string directionPrefix,
        string? requestNumber,
        DateTime createdAt)
    {
        var contentType = directionPrefix == "I"
            ? httpContext.Request.ContentType
            : httpContext.Response.ContentType;

        if (directionPrefix == "I" && IsXmlContentType(contentType))
        {
            return payload;
        }

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("ArchivedPayload",
                new XElement("Direction", directionPrefix),
                new XElement("CapturedAt", createdAt.ToString("yyyy-MM-dd HH:mm:ss.fff")),
                new XElement("Method", httpContext.Request.Method),
                new XElement("Path", httpContext.Request.Path.ToString()),
                new XElement("ContentType", contentType ?? string.Empty),
                new XElement("RequestNumber", requestNumber ?? string.Empty),
                new XElement("Payload",
                    new XCData(payload))));

        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static bool IsXmlContentType(string? contentType)
    {
        return !string.IsNullOrWhiteSpace(contentType)
            && contentType.Contains("xml", StringComparison.OrdinalIgnoreCase);
    }
}
