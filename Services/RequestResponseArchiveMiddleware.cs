using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace KaApi.Services;

public sealed class RequestResponseArchiveMiddleware(
    RequestDelegate next,
    IPayloadArchiveService payloadArchiveService,
    ILogger<RequestResponseArchiveMiddleware> logger)
{
    public const string RequestNumberItemKey = "KaApi.RequestNumber";

    private readonly RequestDelegate _next = next;
    private readonly IPayloadArchiveService _payloadArchiveService = payloadArchiveService;
    private readonly ILogger<RequestResponseArchiveMiddleware> _logger = logger;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!ShouldArchive(context))
        {
            await _next(context);
            return;
        }

        var incomingPayload = await ReadRequestBodyAsync(context, context.RequestAborted);
        context.Items[RequestNumberItemKey] = TryExtractRequestNumber(context, incomingPayload);
        await _payloadArchiveService.SaveIncomingAsync(context, incomingPayload, context.RequestAborted);

        var originalResponseBody = context.Response.Body;
        await using var responseBuffer = new MemoryStream();
        context.Response.Body = responseBuffer;

        try
        {
            await _next(context);

            responseBuffer.Position = 0;
            var outgoingPayload = await new StreamReader(responseBuffer, Encoding.UTF8, leaveOpen: true)
                .ReadToEndAsync(context.RequestAborted);
            await _payloadArchiveService.SaveOutgoingAsync(context, outgoingPayload, context.RequestAborted);

            responseBuffer.Position = 0;
            await responseBuffer.CopyToAsync(originalResponseBody, context.RequestAborted);
        }
        finally
        {
            context.Response.Body = originalResponseBody;
        }
    }

    private static bool ShouldArchive(HttpContext context)
    {
        return context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);
    }

    private string? TryExtractRequestNumber(HttpContext context, string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            if (IsJsonRequest(context))
            {
                using var document = JsonDocument.Parse(payload);
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    TryGetJsonPropertyValue(document.RootElement, "saleNo", out var saleNo))
                {
                    return saleNo;
                }

                return null;
            }

            if (IsXmlRequest(context))
            {
                var document = XDocument.Parse(payload);
                var root = document.Root;
                if (root is null)
                {
                    return null;
                }

                if (NameEquals(root.Name.LocalName, "SaleRequest"))
                {
                    return ChildValue(root, "SaleNo");
                }

                if (NameEquals(root.Name.LocalName, "transaction"))
                {
                    return ChildValue(root, "id") ?? ChildValue(root, "transaction-id");
                }

                if (NameEquals(root.Name.LocalName, "orders"))
                {
                    var orderElement = root.Elements()
                        .FirstOrDefault(element => NameEquals(element.Name.LocalName, "order"));
                    return ChildValue(orderElement, "id");
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to extract request number for archive file naming. Method={Method}, Path={Path}",
                context.Request.Method,
                context.Request.Path);
        }

        return null;
    }

    private async Task<string> ReadRequestBodyAsync(HttpContext context, CancellationToken cancellationToken)
    {
        try
        {
            context.Request.EnableBuffering();
            context.Request.Body.Position = 0;

            using var reader = new StreamReader(
                context.Request.Body,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                leaveOpen: true);

            var body = await reader.ReadToEndAsync(cancellationToken);
            context.Request.Body.Position = 0;
            return body;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to read request body for archiving. Method={Method}, Path={Path}",
                context.Request.Method,
                context.Request.Path);
            return string.Empty;
        }
    }

    private static bool IsJsonRequest(HttpContext context)
    {
        return context.Request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsXmlRequest(HttpContext context)
    {
        var contentType = context.Request.ContentType;
        return contentType?.Contains("xml", StringComparison.OrdinalIgnoreCase) == true
            || contentType?.Contains("text/xml", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool TryGetJsonPropertyValue(JsonElement element, string propertyName, out string? value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()
                    : property.Value.ToString();
                return !string.IsNullOrWhiteSpace(value);
            }
        }

        value = null;
        return false;
    }

    private static bool NameEquals(string? left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static XElement? Child(XElement? parent, params string[] names)
    {
        if (parent is null)
        {
            return null;
        }

        return parent.Elements()
            .FirstOrDefault(element => names.Any(name => NameEquals(element.Name.LocalName, name)));
    }

    private static string? ChildValue(XElement? parent, params string[] names)
    {
        return Child(parent, names)?.Value?.Trim();
    }
}
