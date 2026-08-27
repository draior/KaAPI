using System.ComponentModel.DataAnnotations;

namespace KaApi.Models;

public class SaleDetailUpsertRequest
{
    public string? Id { get; set; }

    public string? ArticleId { get; set; }

    [MaxLength(35)]
    public string? Code { get; set; }

    [MaxLength(50)]
    public string? Name { get; set; }

    [MaxLength(50)]
    public string? NameLatin { get; set; }

    public string? DimensionId { get; set; }

    [Range(0, double.MaxValue)]
    public double Quantity { get; set; }

    [Range(0, double.MaxValue)]
    public double SinglePrice { get; set; }

    [Range(0, double.MaxValue)]
    public double? SourceSinglePrice { get; set; }

    [Range(0, double.MaxValue)]
    public double Discount { get; set; }

    [Range(0, double.MaxValue)]
    public double Vat { get; set; }

    public DateTime? DeliveryDate { get; set; }

    public string? SupplierId { get; set; }

    public string? CodeEan { get; set; }
}
