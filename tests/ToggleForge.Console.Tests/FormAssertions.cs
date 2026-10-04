using Bunit;
using Microsoft.AspNetCore.Components;

namespace ToggleForge.Console.Tests;

/// <summary>Como os testes leem os campos de um formulário do console: texto, mensagem de erro e foco.</summary>
internal static class FormAssertions
{
    public static string TextOf(AngleSharp.Dom.IElement area) => area.GetAttribute("value") ?? area.TextContent;

    // O ⚠ é só reforço visual (aria-hidden); a mensagem é o resto.
    public static string MessageOf(AngleSharp.Dom.IElement element) =>
        string.Join(' ', element.TextContent.Replace("⚠", string.Empty, StringComparison.Ordinal)
            .Split((char[])[' ', '\n'], StringSplitOptions.RemoveEmptyEntries));

    // O bUnit só escreve o id da referência no primeiro render; ele é lido antes de editar.
    public static string ReferenceOf(AngleSharp.Dom.IElement element) => element.GetAttribute("blazor:elementreference") ?? string.Empty;

    public static void AssertFocused(BunitContext console, string reference) =>
        Assert.Equal(reference, Assert.IsType<ElementReference>(console.JSInterop.VerifyFocusAsyncInvoke().Arguments[0]).Id);
}
