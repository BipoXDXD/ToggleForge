using System.Reflection;

namespace ToggleForge.Sdk.Tests;

/// <summary>Regras da ADR 0010 que o compilador não vê: o SDK roda na Unity e não pode arrastar dependências.</summary>
public sealed class SdkBoundaryTests
{
    private static readonly Assembly _sdk = typeof(ToggleForgeClient).Assembly;

    [Fact]
    public void The_sdk_depends_only_on_the_evaluation_core_newtonsoft_and_the_time_provider()
    {
        var references = _sdk.GetReferencedAssemblies().Select(reference => reference.Name).Order(StringComparer.Ordinal);

        Assert.Equal(["Microsoft.Bcl.TimeProvider", "Newtonsoft.Json", "ToggleForge.Evaluation", "netstandard"], references);
    }

    [Fact]
    public void No_public_member_of_the_sdk_exposes_a_newtonsoft_type()
    {
        var exposed = _sdk.GetExportedTypes()
            .SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .SelectMany(TypesIn)
            .Where(type => type.Namespace?.StartsWith("Newtonsoft", StringComparison.Ordinal) == true);

        // O jogo não pode depender da versão do Newtonsoft que o SDK usa por dentro.
        Assert.Empty(exposed);
    }

    private static IEnumerable<Type> TypesIn(MemberInfo member) => member switch
    {
        MethodBase method => method.GetParameters().Select(parameter => parameter.ParameterType)
            .Concat(method is MethodInfo info ? [info.ReturnType] : []),
        PropertyInfo property => [property.PropertyType],
        FieldInfo field => [field.FieldType],
        EventInfo @event => @event.EventHandlerType is { } handler ? [handler] : [],
        _ => [],
    };
}
