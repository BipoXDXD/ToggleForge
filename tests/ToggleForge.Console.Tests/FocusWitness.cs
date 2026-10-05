using Bunit;
using Microsoft.JSInterop.Infrastructure;

namespace ToggleForge.Console.Tests;

/// <summary>
/// Responde ao <c>FocusAsync</c> como o bUnit e, antes, anota o que a página mostra no instante em que o foco chega:
/// um leitor de tela anuncia o campo como ele está nesse instante, não como fica depois.
/// </summary>
internal sealed class FocusWitness : JSRuntimeInvocationHandler
{
    private const string FocusIdentifier = "Blazor._internal.domWrapper.focus";

    private readonly Func<string?> _read;

    public FocusWitness(Func<string?> read)
        : base(invocation => invocation.Identifier == FocusIdentifier, isCatchAllHandler: false)
    {
        _read = read;
        SetVoidResult();
    }

    public List<string?> Seen { get; } = [];

    protected override Task<IJSVoidResult> HandleAsync(JSRuntimeInvocation invocation)
    {
        Seen.Add(_read());
        return base.HandleAsync(invocation);
    }
}
