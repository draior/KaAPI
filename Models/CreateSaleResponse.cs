namespace KaApi.Models;

public class CreateSaleResponse
{
    public string SaleId { get; set; } = string.Empty;

    public string? SaleNo { get; set; }

    public string? ClientId { get; set; }

    public string? ClientAgentId { get; set; }

    public bool ClientCreated { get; set; }

    public bool ClientAgentCreated { get; set; }

    public double TotalWithoutVat { get; set; }

    public double Vat { get; set; }

    public double TotalWithVat { get; set; }

    public int DetailCount { get; set; }

    public List<string> DetailIds { get; set; } = [];
}
