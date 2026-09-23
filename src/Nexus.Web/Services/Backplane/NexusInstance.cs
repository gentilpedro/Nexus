namespace Nexus.Web.Services.Backplane;

/// <summary>
/// Identity of this running process, used to tell our own backplane traffic apart from another
/// instance's.
/// </summary>
/// <remarks>
/// Generated fresh at startup rather than read from configuration or the machine name: two
/// instances of the same container image share a machine name, and an operator who forgets to
/// set a per-instance variable would get two processes claiming the same identity — which looks
/// exactly like the echo the id exists to suppress, except now real messages are dropped instead
/// of duplicates. A new GUID per process cannot collide and needs no configuration.
/// </remarks>
public sealed class NexusInstance
{
    /// <summary>Unique for the lifetime of this process.</summary>
    public Guid Id { get; } = Guid.NewGuid();
}
