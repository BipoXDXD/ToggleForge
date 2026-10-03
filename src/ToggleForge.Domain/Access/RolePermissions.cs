namespace ToggleForge.Domain.Access;

public static class RolePermissions
{
    /// <summary>Alterar o rascunho, publicar e reverter.</summary>
    public static bool CanAuthor(this Role role) => role is Role.Owner or Role.Editor;

    /// <summary>Criar projetos e ambientes e gerenciar credenciais.</summary>
    public static bool CanAdminister(this Role role) => role is Role.Owner;
}
