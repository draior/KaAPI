using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;

namespace KaApi.Models;

public class ClientUpsertRequest
{
    public string? Id { get; set; }

    [MaxLength(10)]
    public string? Code { get; set; }

    [MaxLength(64)]
    public string? Name { get; set; }

    public string? CountryId { get; set; }

    [MaxLength(60)]
    public string? Address { get; set; }

    [MaxLength(32)]
    public string? Phone { get; set; }

    [MaxLength(32)]
    public string? Phone2 { get; set; }

    [MaxLength(20)]
    public string? TaxNo { get; set; }

    [MaxLength(20)]
    public string? Bulstat { get; set; }

    public string? ClientTypeId { get; set; }

    [MaxLength(1)]
    public string? RegVat { get; set; }

    [MaxLength(40)]
    public string? Mol { get; set; }

    [MaxLength(15)]
    public string? Egn { get; set; }

    [MaxLength(512)]
    public string? Comment { get; set; }

    [MaxLength(128)]
    public string? ContactAddress { get; set; }

    [MaxLength(64)]
    public string? Email { get; set; }

    [MaxLength(64)]
    public string? WebAddress { get; set; }

    [MaxLength(32)]
    public string? CellPhone { get; set; }

    public string? CityId { get; set; }

    public ClientAgentUpsertRequest? Agent { get; set; }
}
