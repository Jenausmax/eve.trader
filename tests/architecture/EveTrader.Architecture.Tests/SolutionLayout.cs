using System.Xml.Linq;

namespace EveTrader.Architecture.Tests;

/// <summary>
/// Раскладка решения, прочитанная с диска. Законы слоёв проверяются по графу
/// <c>ProjectReference</c>, а не по зависимостям типов: ссылка, добавленная в
/// csproj, нарушает закон в момент добавления, а не когда ею впервые
/// воспользовались. NetArchTest этого не видит — он смотрит на типы.
/// </summary>
internal static class SolutionLayout
{
    public static DirectoryInfo Root { get; } = FindRoot();

    /// <summary>Слой проекта по его пути внутри <c>src/</c>.</summary>
    public static string LayerOf(FileInfo project) =>
        Path.GetRelativePath(Root.FullName, project.FullName)
            .Replace('\\', '/')
            .Split('/')[1];

    public static IReadOnlyList<FileInfo> SourceProjects() =>
        [.. new DirectoryInfo(Path.Combine(Root.FullName, "src"))
            .EnumerateFiles("*.csproj", SearchOption.AllDirectories)
            .OrderBy(static file => file.FullName, StringComparer.Ordinal)];

    /// <summary>Проекты, на которые ссылается указанный, в виде имён без расширения.</summary>
    public static IReadOnlyList<string> ReferencedProjects(FileInfo project) =>
        [.. XDocument.Load(project.FullName)
            .Descendants("ProjectReference")
            .Select(static reference => reference.Attribute("Include")?.Value)
            .Where(static include => !string.IsNullOrWhiteSpace(include))
            .Select(static include => Path.GetFileNameWithoutExtension(include!.Replace('\\', '/')))
            .OrderBy(static name => name, StringComparer.Ordinal)];

    public static string LayerOfProjectNamed(string projectName)
    {
        FileInfo? project = SourceProjects()
            .FirstOrDefault(file => Path.GetFileNameWithoutExtension(file.Name) == projectName);

        return project is null ? "unknown" : LayerOf(project);
    }

    private static DirectoryInfo FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EveTrader.slnx")))
        {
            directory = directory.Parent;
        }

        return directory ?? throw new InvalidOperationException("EveTrader.slnx не найден вверх от каталога тестов");
    }
}
