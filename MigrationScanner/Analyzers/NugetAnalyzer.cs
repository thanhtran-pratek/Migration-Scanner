// MigrationScanner/Analyzers/NugetAnalyzer.cs
using System.Xml.Linq;
using MigrationScanner.Rules;
using MigrationScanner.Scanner;

namespace MigrationScanner.Analyzers;

public class NugetAnalyzer : IAnalyzer
{
    public async Task<IEnumerable<Finding>> AnalyzeAsync(ScanContext context)
    {
        var findings = new List<Finding>();
        foreach (var file in context.ProjectFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(file);
                findings.AddRange(AnalyzeProject(file, content));
            }
            catch (IOException)
            {
                // skip unreadable files
            }
        }
        return findings;
    }

    private IEnumerable<Finding> AnalyzeProject(string filePath, string content)
    {
        XDocument doc;
        try { doc = XDocument.Parse(content); }
        catch { yield break; }

        foreach (var packageRef in doc.Descendants("PackageReference"))
        {
            var packageName = packageRef.Attribute("Include")?.Value;
            if (packageName == null) continue;

            if (!KnownWindowsPackages.All.TryGetValue(packageName, out var info)) continue;

            var version = packageRef.Attribute("Version")?.Value ?? "unknown";
            var suggestion = info.Alternative != null
                ? $"Alternative: {info.Alternative}"
                : "No direct Linux alternative — consider removing";

            yield return new Finding(filePath, 0, info.Severity, "Windows Package",
                $"Windows-only package: {packageName} {version} — {info.Reason}",
                $"<PackageReference Include=\"{packageName}\" Version=\"{version}\" />",
                suggestion);
        }
    }
}
