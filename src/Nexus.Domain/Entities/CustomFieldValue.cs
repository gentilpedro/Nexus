namespace Nexus.Domain.Entities;

// One row per (WorkItem, CustomFieldDefinition) that has actually been filled in — sparse,
// not one column per field. Value is always a string, even for Number/Date fields; for Select
// fields it holds the chosen CustomFieldOption.Id (as text, not a real FK — see
// CustomFieldValueConfiguration for why).
public class CustomFieldValue
{
    public Guid Id { get; set; }

    public Guid WorkItemId { get; set; }
    public WorkItem WorkItem { get; set; } = null!;

    public Guid CustomFieldDefinitionId { get; set; }
    public CustomFieldDefinition CustomFieldDefinition { get; set; } = null!;

    public string Value { get; set; } = "";
}
