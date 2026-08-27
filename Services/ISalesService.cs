using KaApi.Models;

namespace KaApi.Services;

public interface ISalesService
{
    Task<CreateSaleResponse> CreateSaleAsync(SaleUpsertRequest request, CancellationToken cancellationToken);
}
