using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Fluent.Syntax.Elements.Types;
using ArchUnitNET.Loader;
using ToggleForge.Application.Authoring;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace ToggleForge.Api.Tests;

/// <summary>
/// Fronteiras entre módulos que o compilador não vê, porque cada camada junta os três módulos num assembly só
/// (ADR 0005). As camadas em si já são garantidas pelas referências entre projetos.
/// </summary>
public sealed class ArchitectureTests
{
    private const string Layers = @"^ToggleForge\.(Domain|Application|Infrastructure)";

    private static readonly Architecture _architecture = new ArchLoader().LoadAssemblies(
            typeof(Domain.Failure).Assembly,
            typeof(Application.IUnitOfWork).Assembly,
            typeof(Infrastructure.InfrastructureServices).Assembly)
        .Build();

    [Fact]
    public void Access_knows_nothing_of_the_modules_that_use_it() =>
        AssertHolds(Module("Access").Should().NotDependOnAny(Module("Authoring", "Delivery"))
            .Because("Access é o módulo de origem: membros e papéis não dependem de projetos nem de credenciais"));

    [Fact]
    public void Authoring_knows_nothing_of_delivery() =>
        AssertHolds(Module("Authoring").Should().NotDependOnAny(Module("Delivery"))
            .Because("o rascunho e a publicação existem sem o jogo; a entrega consome a publicação, não o contrário"));

    [Fact]
    public void Delivery_uses_authoring_only_through_its_public_api() =>
        AssertHolds(Module("Delivery").Should().NotDependOnAny(Module("Authoring")
                .And().AreNot(typeof(IEnvironmentDirectory), typeof(IPublishedSnapshots), typeof(EnvironmentLocator), typeof(SnapshotView)))
            .Because("o módulo Authoring pode mudar por dentro sem quebrar a entrega"));

    [Theory]
    [InlineData("Access", "Authoring")]
    [InlineData("Access", "Delivery")]
    [InlineData("Authoring", "Access")]
    [InlineData("Authoring", "Delivery")]
    [InlineData("Delivery", "Access")]
    [InlineData("Delivery", "Authoring")]
    public void No_module_touches_the_persistence_of_another(string module, string other) =>
        AssertHolds(Types().That().ResideInNamespaceMatching($@"^ToggleForge\.Infrastructure\.{module}(\..+)?$")
            .Should().NotDependOnAny(Types().That().ResideInNamespaceMatching($@"^ToggleForge\.Infrastructure\.{other}(\..+)?$"))
            .Because("cada módulo é dono das próprias tabelas e do próprio DbContext"));

    private static GivenTypesConjunction Module(params string[] modules) =>
        Types().That().ResideInNamespaceMatching($@"{Layers}\.({string.Join('|', modules)})(\..+)?$");

    private static void AssertHolds(TypesShouldConjunctionWithDescription rule)
    {
        var violations = rule.Evaluate(_architecture).Where(result => !result.Passed).Select(result => result.Description).ToList();
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }
}
