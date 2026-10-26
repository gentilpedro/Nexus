namespace Nexus.Domain.Entities;

// A request sent through the public "talk to us" form about the Business plan (#56). Not tied to
// an account: the person may not have one. Purged by DataRetentionHostedService once it has
// outlived the sales conversation it was for.
public class CommercialContact
{
    public const int MaxNameLength = 200;
    public const int MaxCompanyLength = 200;
    public const int MaxEmailLength = 256;
    public const int MaxTeamSizeLength = 50;
    public const int MaxMessageLength = 2000;

    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Company { get; set; } = "";
    public string Email { get; set; } = "";
    public string TeamSize { get; set; } = "";
    public string? Message { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
