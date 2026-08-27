namespace KaApi.Models;

public class OrderXmlImportDefaults
{
    public string? BusinessGroupId { get; set; }

    public string? UserId { get; set; }

    public int SaleTypeId { get; set; } = 3;

    public string CurrencyId { get; set; } = "SYSTEM";

    public double CurrencyRate { get; set; } = 1d;

    public string? ClientTypeId { get; set; } = "VK03";

    public int? PayStatusId { get; set; } = 1;

    public int? DocStatusId { get; set; } = 2;
}
