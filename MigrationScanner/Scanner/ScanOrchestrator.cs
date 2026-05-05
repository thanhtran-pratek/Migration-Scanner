// MigrationScanner/Scanner/ScanOrchestrator.cs
using MigrationScanner.Analyzers;

namespace MigrationScanner.Scanner;

public class ScanOrchestrator
{
    private readonly IEnumerable<IAnalyzer> _analyzers;

    public ScanOrchestrator(IEnumerable<IAnalyzer> analyzers) => _analyzers = analyzers;

    public async Task<IEnumerable<Finding>> ScanAsync(string path)
    {
        var context = BuildContext(path);
        var tasks = _analyzers.Select(a => a.AnalyzeAsync(context));
        var results = await Task.WhenAll(tasks);
        return results.SelectMany(r => r)
                      .OrderBy(f => f.FilePath)
                      .ThenBy(f => f.Line);
    }

    private static ScanContext BuildContext(string path)
    {
        var root = File.Exists(path) ? Path.GetDirectoryName(path)! : path;

        if (!Directory.Exists(root))
            throw new ArgumentException($"Path does not exist: {root}", nameof(path));

        var s = Path.DirectorySeparatorChar;
        var objSeg = $"{s}obj{s}";
        var binSeg = $"{s}bin{s}";

        bool IsGenerated(string f) => f.Contains(objSeg) || f.Contains(binSeg);

        var csFiles = Directory
            .GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsGenerated(f))
            .ToList();

        var configFiles = Directory
            .GetFiles(root, "appsettings*.json", SearchOption.AllDirectories)
            .Where(f => !IsGenerated(f))
            .ToList();

        var projectFiles = Directory
            .GetFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(f => !IsGenerated(f))
            .ToList();

        return new ScanContext(root, csFiles, configFiles, projectFiles);
    }
}
