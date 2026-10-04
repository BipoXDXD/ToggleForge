using Microsoft.AspNetCore.Components;

namespace ToggleForge.Console.Components;

/// <summary>Página que cancela as chamadas à API ainda em andamento quando o membro sai dela.</summary>
public abstract class ConsolePage : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _leaving = new();

    protected CancellationToken Leaving => _leaving.Token;

    public void Dispose()
    {
        _leaving.Cancel();
        _leaving.Dispose();
        GC.SuppressFinalize(this);
    }
}
