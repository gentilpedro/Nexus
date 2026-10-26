namespace Nexus.Domain.Entities;

// Persisted as an integer: the declaration order is part of the stored data, so new plans go at
// the end, never in the middle.
public enum WorkspacePlan
{
    Starter,
    Pro,
    Business
}
