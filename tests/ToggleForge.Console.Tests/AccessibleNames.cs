using Bunit;
using Microsoft.AspNetCore.Components;

namespace ToggleForge.Console.Tests;

/// <summary>Os nomes que um leitor de tela lê, tirados do markup como o navegador os calcula.</summary>
internal static class AccessibleNames
{
    public static string CurrentBreadcrumb<TPage>(IRenderedComponent<TPage> page)
        where TPage : IComponent =>
        page.Find("nav[aria-label=Breadcrumb] [aria-current=page]").TextContent.Trim();

    /// <summary>O texto do elemento apontado por <c>aria-labelledby</c>, ou o <c>aria-label</c> da tabela.</summary>
    public static string? TableName<TPage>(IRenderedComponent<TPage> page)
        where TPage : IComponent
    {
        var table = page.Find("table");
        return table.GetAttribute("aria-labelledby") is { } id
            ? page.Find($"#{id}").TextContent.Trim()
            : table.GetAttribute("aria-label");
    }
}
