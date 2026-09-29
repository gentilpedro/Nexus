namespace Nexus.Web.Services;

/// <summary>
/// O que o <c>ConfirmDialog</c> mostra. Os textos nomeiam a ação ("Excluir space"), nunca um
/// genérico "OK" — quem lê o botão precisa saber o que acontece ao clicar.
/// </summary>
public sealed record ConfirmRequest(
    string Title,
    string Message,
    string ConfirmLabel,
    string CancelLabel = "Cancelar",
    bool Destructive = true)
{
    /// <summary>Confirmação padrão de exclusão: "Excluir {coisa}?" + o que se perde.</summary>
    public static ConfirmRequest Delete(string thing, string? name, string consequence) =>
        new(
            Title: string.IsNullOrWhiteSpace(name) ? $"Excluir {thing}?" : $"Excluir {thing} \"{name}\"?",
            Message: $"{consequence} Essa ação não pode ser desfeita.",
            ConfirmLabel: $"Excluir {thing}");
}

public enum ToastKind
{
    Success,
    Info,
    Error
}

public sealed record Toast(Guid Id, string Message, ToastKind Kind);

/// <summary>
/// Retorno rápido de ações concluídas ("Tarefa salva", "Label excluída"). Scoped por circuito,
/// no mesmo molde de <see cref="NotificationBadgeService"/>: a página chama <see cref="Show"/>
/// e o ToastHost do MainLayout redesenha. Erros que exigem ação continuam como alerta inline,
/// junto do campo ou do formulário — toast é só confirmação passageira.
/// </summary>
public class ToastService
{
    private readonly List<Toast> toasts = [];

    public IReadOnlyList<Toast> Toasts => toasts;

    public event Action? Changed;

    public void Show(string message, ToastKind kind = ToastKind.Success)
    {
        var toast = new Toast(Guid.NewGuid(), message, kind);
        toasts.Add(toast);

        // Nunca mais de três empilhados: o mais antigo sai.
        if (toasts.Count > 3)
        {
            toasts.RemoveAt(0);
        }

        Changed?.Invoke();
        _ = DismissLaterAsync(toast.Id, kind == ToastKind.Error ? 7000 : 4000);
    }

    public void Dismiss(Guid id)
    {
        if (toasts.RemoveAll(t => t.Id == id) > 0)
        {
            Changed?.Invoke();
        }
    }

    private async Task DismissLaterAsync(Guid id, int delayMs)
    {
        await Task.Delay(delayMs);
        Dismiss(id);
    }
}
