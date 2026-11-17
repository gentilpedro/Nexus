using Nexus.Domain.Entities;

namespace Nexus.Web.Services;

// Nome de exibição de uma conta, com o e-mail como reserva, para telas e relatórios que não
// querem depender de Nexus.Web.Api.
public static class ApiMappingNames
{
    public static string DisplayNameOf(ApplicationUser? user) =>
        user is null ? "Conta removida" : string.IsNullOrWhiteSpace(user.DisplayName) ? (user.Email ?? "") : user.DisplayName;
}
