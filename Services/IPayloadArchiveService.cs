namespace KaApi.Services;

public interface IPayloadArchiveService
{
    Task<string?> SaveIncomingAsync(HttpContext httpContext, string payload, CancellationToken cancellationToken);

    Task<string?> SaveOutgoingAsync(HttpContext httpContext, string payload, CancellationToken cancellationToken);
}
