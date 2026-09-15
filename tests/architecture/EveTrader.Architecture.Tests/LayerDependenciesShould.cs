using Shouldly;

namespace EveTrader.Architecture.Tests;

/// <summary>
/// Законы направления ссылок из <c>.agents/rules/csharp/local-project-layers.md</c>
/// §«Направление ссылок». Проверяются здесь, а не на ревью.
/// </summary>
public sealed class LayerDependenciesShould
{
    /// <summary>На что слою разрешено ссылаться.</summary>
    private static readonly Dictionary<string, string[]> Allowed = new(StringComparer.Ordinal)
    {
        ["domain"] = [],
        ["application"] = ["domain"],
        ["infrastructure"] = ["application", "domain"],
        ["presentation"] = ["application", "domain", "infrastructure"],
        ["build"] = [],
    };

    private static string Report(string headline, IReadOnlyCollection<string> violations) =>
        $"{headline}:{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", violations)}";

    [Fact]
    public void PointDownOnly()
    {
        var violations = new List<string>();

        foreach (FileInfo project in SolutionLayout.SourceProjects())
        {
            var layer = SolutionLayout.LayerOf(project);
            var allowed = Allowed.TryGetValue(layer, out var value)
                ? value
                : throw new InvalidOperationException(
                    $"Слой '{layer}' не объявлен в local-project-layers.md — папка верхнего уровня заводится решением, а не по ходу");

            foreach (var referenced in SolutionLayout.ReferencedProjects(project))
            {
                var referencedLayer = SolutionLayout.LayerOfProjectNamed(referenced);

                if (!allowed.Contains(referencedLayer, StringComparer.Ordinal))
                {
                    violations.Add($"{Path.GetFileNameWithoutExtension(project.Name)} ({layer}) → {referenced} ({referencedLayer})");
                }
            }
        }

        violations.ShouldBeEmpty(Report("Ссылка вверх или вбок", violations));
    }

    [Fact]
    public void NotLetInfrastructureProjectsReferenceEachOther()
    {
        var violations = new List<string>();

        foreach (FileInfo? project in SolutionLayout.SourceProjects()
            .Where(static file => SolutionLayout.LayerOf(file) == "infrastructure"))
        {
            foreach (var referenced in SolutionLayout.ReferencedProjects(project)
                .Where(static name => SolutionLayout.LayerOfProjectNamed(name) == "infrastructure"))
            {
                violations.Add($"{Path.GetFileNameWithoutExtension(project.Name)} → {referenced}");
            }
        }

        violations.ShouldBeEmpty(Report("Инфраструктура ссылается на инфраструктуру", violations));
    }

    [Fact]
    public void KeepDomainFreeOfProjectReferences() =>
        SolutionLayout.SourceProjects()
            .Where(static file => SolutionLayout.LayerOf(file) == "domain")
            .SelectMany(SolutionLayout.ReferencedProjects)
            .ShouldBeEmpty("Домен не ссылается ни на что: в нём только типы и функции над ними");
}
