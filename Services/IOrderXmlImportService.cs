using KaApi.Models;

namespace KaApi.Services;

public interface IOrderXmlImportService
{
    Task<SaleUpsertRequest> MapAsync(Stream xmlStream, OrderXmlImportOverrides? overrides, CancellationToken cancellationToken);
}
