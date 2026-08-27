using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;

namespace KaApi.Models;

[XmlRoot("SaleRequest")]
public class SaleUpsertRequest
{
    [Required]
    [MinLength(1)]
    [XmlArray("Details")]
    [XmlArrayItem("Detail")]
    public List<SaleDetailUpsertRequest> Details { get; set; } = [];

    public string? Id { get; set; }

    public string? SaleNo { get; set; }

    public DateTime? SaleDate { get; set; }

    public int? SaleTypeId { get; set; }

    public string? ClientId { get; set; }

    [XmlElement("Client")]
    public ClientUpsertRequest? Client { get; set; }

    public string? BusinessGroupId { get; set; }

    public string? UserId { get; set; }

    public double? Discount { get; set; }

    public double? PayedSum { get; set; }

    public DateTime? Deadline { get; set; }

    public int? PayStatusId { get; set; }

    public int? DocStatusId { get; set; }

    public string? CurrencyId { get; set; }

    public double? CurrencyRate { get; set; }

    public string? Comment { get; set; }

    public string? ClientAgentId { get; set; }

    public string? DesignerId { get; set; }

    public string? ContractNo { get; set; }

    public DateTime? ContractDate { get; set; }

    public string? Unp { get; set; }
}
