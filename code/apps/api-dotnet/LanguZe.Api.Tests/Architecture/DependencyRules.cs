using System.Text.RegularExpressions;
using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using LanguZe.Api.Data;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace LanguZe.Api.Tests.Architecture;

/// <summary>
/// The dependency rules of ADR-0006, section 1. The modules share one project, so the
/// compiler cannot keep them apart; these tests do, and each failure says which rule
/// broke and why, for whoever (or whatever agent) broke it.
/// </summary>
public sealed class DependencyRules
{
    private const string Modules = @"LanguZe\.Api\.Modules";

    private static readonly ArchUnitNET.Domain.Architecture Api =
        new ArchLoader().LoadAssemblies(typeof(Program).Assembly).Build();

    private static readonly string[] ApiNamespaces = typeof(Program).Assembly.GetTypes()
        .Select(type => type.Namespace)
        .OfType<string>()
        .Distinct()
        .ToArray();

    public static TheoryData<string> ModuleNames => new(
        ApiNamespaces
            .Select(ns => Regex.Match(ns, $"^{Modules}\\.([^.]+)"))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .Distinct());

    public static TheoryData<string> FeatureNamespaces => new(
        ApiNamespaces
            .Select(ns => Regex.Match(ns, $"^{Modules}\\.[^.]+\\.Features\\.[^.]+"))
            .Where(match => match.Success)
            .Select(match => match.Value)
            .Distinct());

    [Fact]
    public void R1_domain_code_depends_on_no_framework_and_no_database()
    {
        var domain = Types().That().ResideInNamespaceMatching($@"^{Modules}\.[^.]+\.Domain(\..+)?$");
        if (!domain.GetObjects(Api).Any())
        {
            return;
        }

        domain.Should().NotDependOnAny(
                Types().That().ResideInNamespaceMatching(@"^(Microsoft\.EntityFrameworkCore|Microsoft\.AspNetCore|Npgsql|System\.Net\.Http|LanguZe\.Api\.Data)(\..+)?$"))
            .Because("business rules are tested without a database or HTTP (ADR-0006, section 1, rule R1)")
            .Check(Api);
    }

    [Theory]
    [MemberData(nameof(FeatureNamespaces))]
    public void R2_a_slice_depends_on_no_other_slice(string feature)
    {
        var module = feature[..feature.IndexOf(".Features.", StringComparison.Ordinal)];
        var others = $@"^{Regex.Escape(module)}\.Features\.(?!{Regex.Escape(feature[(module.Length + ".Features.".Length)..])}(\.|$))";

        Types().That().ResideInNamespaceMatching($"^{Regex.Escape(feature)}(\\..+)?$")
            .Should().NotDependOnAny(Types().That().ResideInNamespaceMatching(others))
            .Because("what slices share belongs in the module's Shared or Domain (rule R2)")
            .Check(Api);
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void R3_a_module_reaches_another_only_through_its_contracts(string module)
    {
        var otherModulesInternals = $@"^{Modules}\.(?!{Regex.Escape(module)}(\.|$))[^.]+(\.(?!Contracts(\.|$)).+)?$";

        Types().That().ResideInNamespaceMatching($@"^{Modules}\.{Regex.Escape(module)}(\..+)?$")
            .Should().NotDependOnAny(Types().That().ResideInNamespaceMatching(otherModulesInternals))
            .Because("modules meet only at a Contracts namespace, so one can leave the monolith later; entities in LanguZe.Api.Data are shared (rule R3)")
            .Check(Api);
    }

    [Fact]
    public void R4_an_endpoint_never_touches_the_database()
    {
        var endpoints = Classes().That().HaveNameEndingWith("Endpoint");
        if (!endpoints.GetObjects(Api).Any())
        {
            return;
        }

        endpoints.Should().NotDependOnAny(typeof(LanguZeDbContext))
            .AndShould().NotDependOnAny(Types().That().ResideInNamespaceMatching(@"^Microsoft\.EntityFrameworkCore(\..+)?$"))
            .Because("an endpoint only translates HTTP; its handler does the work (rule R4)")
            .Check(Api);
    }

    [Fact]
    public void R5_the_platform_knows_no_module() =>
        Types().That().ResideInNamespaceMatching(@"^LanguZe\.Api\.Platform(\..+)?$")
            .Should().NotDependOnAny(Types().That().ResideInNamespaceMatching($@"^{Modules}(\..+)?$"))
            .Because("every module builds on the platform, so the platform cannot lean back on one (rule R5)")
            .Check(Api);

    [Fact]
    public void The_rules_see_the_modules()
    {
        Assert.Contains("Health", ModuleNames.Select(row => row.Data));
        Assert.NotEmpty(FeatureNamespaces);
    }
}
