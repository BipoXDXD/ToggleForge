using ToggleForge.Console.Api;

namespace ToggleForge.Console.Environments;

/// <summary>Como as páginas de um ambiente o nomeiam no título.</summary>
public static class EnvironmentText
{
    /// <summary>
    /// "Development (dev)" depois de carregar; antes, só a chave que veio no endereço. O título existe desde o
    /// primeiro render para o <c>FocusOnNavigate</c> achar o <c>h1</c> e o leitor de tela anunciar a página nova.
    /// </summary>
    public static string Title(ProjectEnvironment? loaded, string key) => loaded is null ? key : $"{loaded.Name} ({key})";
}
