// MigrationScanner/Analyzers/ProjectAnalyzer.cs
using System.Xml.Linq;
using MigrationScanner.Scanner;

namespace MigrationScanner.Analyzers;

public class ProjectAnalyzer : IAnalyzer
{
    private static readonly HashSet<string> OldFrameworks = new()
    {
        "net48", "net47", "net46", "net45",
        "netcoreapp2.0", "netcoreapp2.1", "netcoreapp3.0", "netcoreapp3.1"
    };

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
        try { doc = XDocument.Parse(content, LoadOptions.SetLineInfo); }
        catch { yield break; }

        foreach (var el in doc.Descendants())
        {
            var name = el.Name.LocalName;
            var value = el.Value.Trim();
            var line = ((System.Xml.IXmlLineInfo)el).HasLineInfo()
                ? ((System.Xml.IXmlLineInfo)el).LineNumber
                : 0;

            if (name == "RuntimeIdentifier" && value.StartsWith("win", StringComparison.OrdinalIgnoreCase))
                yield return new Finding(filePath, line, Severity.Error, "Windows RID",
                    $"Windows-only RuntimeIdentifier: {value}",
                    $"<RuntimeIdentifier>{value}</RuntimeIdentifier>",
                    "Remove or use linux-x64 / linux-arm64");

            if ((name == "UseWindowsForms" || name == "UseWPF") && value.Equals("true", StringComparison.OrdinalIgnoreCase))
                yield return new Finding(filePath, line, Severity.Error, "WinForms/WPF",
                    $"{name} is not supported on Linux",
                    $"<{name}>true</{name}>",
                    "WinForms/WPF cannot run on Linux — consider a web-based UI");

            if (name == "TargetFramework" && OldFrameworks.Contains(value))
                yield return new Finding(filePath, line, Severity.Warning, "Target Framework",
                    $"Old target framework: {value}",
                    $"<TargetFramework>{value}</TargetFramework>",
                    "Upgrade to net9.0 for full Linux/Docker compatibility");

            if (name == "PlatformTarget" && value.Equals("x86", StringComparison.OrdinalIgnoreCase))
                yield return new Finding(filePath, line, Severity.Warning, "Platform Target",
                    "x86 PlatformTarget may cause issues on Linux/Docker",
                    $"<PlatformTarget>{value}</PlatformTarget>",
                    "Change to AnyCPU or x64");
        }
    }
}
