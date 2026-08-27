using System.ComponentModel.DataAnnotations;

namespace KaApi.Models;

public class ClientAgentUpsertRequest
{
    public string? Id { get; set; }

    [MaxLength(40)]
    public string? Name { get; set; }

    [MaxLength(32)]
    public string? Phone { get; set; }

    [MaxLength(32)]
    public string? Phone2 { get; set; }

    [MaxLength(10)]
    public string? Egn { get; set; }

    [MaxLength(1)]
    public string? PassSeries { get; set; }

    [MaxLength(20)]
    public string? PassNo { get; set; }

    public DateTime? PassDate { get; set; }

    [MaxLength(30)]
    public string? PassUnit { get; set; }

    [MaxLength(512)]
    public string? Comment { get; set; }

    [MaxLength(100)]
    public string? Address { get; set; }

    public string? DutyId { get; set; }

    [MaxLength(64)]
    public string? Email { get; set; }

    [MaxLength(64)]
    public string? WebAddress { get; set; }

    [MaxLength(32)]
    public string? CellPhone { get; set; }

    [MaxLength(64)]
    public string? CrmAddress { get; set; }
}
