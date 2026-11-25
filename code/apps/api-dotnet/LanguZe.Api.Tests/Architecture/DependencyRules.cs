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

    /// <summary>
    /// Rule R3 lets modules meet at their contracts, which still allows two to lean on each other,
    /// directly or through others. ArchUnitNET's slices cut by whole namespace, so Auth.Shared and
    /// Moderation.Contracts would be two slices and a cycle between modules would pass; the graph
    /// is built here instead, one node per module, from the same dependencies the rules above read.
    /// </summary>
    [Fact]
    public void R6_the_modules_depend_on_one_another_in_no_cycle()
    {
        var calls = Api.Types
            .SelectMany(type => type.Dependencies.Select(dependency => (From: ModuleOf(type.Namespace?.FullName), To: ModuleOf(dependency.Target.Namespace?.FullName))))
            .Where(call => call.From is not null && call.To is not null && call.From != call.To)
            .Distinct()
            .ToLookup(call => call.From!, call => call.To!);

        var cycles = calls
            .SelectMany(from => from.Select(to => PathBetween(calls, to, from.Key) is { } back ? Rotated([from.Key, .. back]) : null))
            .OfType<string>()
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            cycles.Count == 0,
            "Modules depend on one another in a cycle, so none of them could leave the monolith alone (rule R6):\n"
            + string.Join('\n', cycles)
            + "\nThe module that asks for something owns the interface in its Contracts, and the module that knows the answer implements it, as auth's IAccountAiAccess is implemented by moderation.");
    }

    // A generic parameter has no namespace.
    private static string? ModuleOf(string? ns) =>
        Regex.Match(ns ?? "", $@"^{Modules}\.([^.]+)") is { Success: true } match ? match.Groups[1].Value : null;

    /// <summary>The modules from <paramref name="start"/> to <paramref name="goal"/>, goal included, or null when it cannot be reached.</summary>
    private static List<string>? PathBetween(ILookup<string, string> calls, string start, string goal)
    {
        var cameFrom = new Dictionary<string, string?> { [start] = null };
        var queue = new Queue<string>([start]);
        while (queue.TryDequeue(out var module))
        {
            if (module == goal)
            {
                var path = new List<string>();
                for (string? step = module; step is not null; step = cameFrom[step])
                {
                    path.Insert(0, step);
                }

                return path;
            }

            foreach (var next in calls[module].Where(next => cameFrom.TryAdd(next, module)))
            {
                queue.Enqueue(next);
            }
        }

        return null;
    }

    /// <summary>A cycle written from its first module in alphabetical order, so each is reported once.</summary>
    private static string Rotated(List<string> cycle)
    {
        var loop = cycle[..^1];
        var first = loop.IndexOf(loop.Min(StringComparer.Ordinal)!);
        return string.Join(" -> ", [.. loop[first..], .. loop[..first], loop[first]]);
    }

    [Fact]
    public void The_rules_see_the_modules()
    {
        Assert.Contains("Health", ModuleNames.Select(row => row.Data));
        Assert.NotEmpty(FeatureNamespaces);
    }
}
