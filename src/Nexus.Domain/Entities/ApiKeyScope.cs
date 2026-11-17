namespace Nexus.Domain.Entities;

// O que uma chave de API pode fazer. Só leitura serve para painéis e relatórios: um vazamento
// dela não cria nem altera tarefa. Os valores são persistidos como inteiro; não reordenar.
public enum ApiKeyScope
{
    ReadOnly = 0,
    ReadWrite = 1,
}
