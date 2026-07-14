namespace Nexus.Domain.Entities;

// Only used when the owning CustomFieldDefinition.Type == Select.
public class CustomFieldOption
{
    public Guid Id { get; set; }

    public Guid CustomFieldDefinitionId { get; set; }
    public CustomFieldDefinition CustomFieldDefinition { get; set; } = null!;

    public string Label { get; set; } = "";
    public int SortOrder { get; set; }
}
