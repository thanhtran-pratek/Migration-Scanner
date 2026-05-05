using MigrationScanner.Analyzers;
using MigrationScanner.Scanner;

namespace MigrationScanner.Tests.Analyzers;

public class ProjectAnalyzerTests
{
    private static async Task<List<Finding>> Analyze(string csprojContent)
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "MyApp.csproj");
        await File.WriteAllTextAsync(file, csprojContent);
        var context = new ScanContext(dir, Array.Empty<string>(), Array.Empty<string>(), new[] { file });
        try
        {
            return (await new ProjectAnalyzer().AnalyzeAsync(context)).ToList();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Detects_windows_runtime_identifier()
    {
        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <RuntimeIdentifier>win-x64</RuntimeIdentifier>
              </PropertyGroup>
            </Project>
            """;
        var findings = await Analyze(csproj);
        Assert.Contains(findings, f => f.Category == "Windows RID" && f.Severity == Severity.Error);
    }

    [Fact]
    public async Task Detects_UseWindowsForms()
    {
        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <UseWindowsForms>true</UseWindowsForms>
              </PropertyGroup>
            </Project>
            """;
        var findings = await Analyze(csproj);
        Assert.Contains(findings, f => f.Category == "WinForms/WPF" && f.Severity == Severity.Error);
    }

    [Fact]
    public async Task Detects_old_target_framework()
    {
        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net48</TargetFramework>
              </PropertyGroup>
            </Project>
            """;
        var findings = await Analyze(csproj);
        Assert.Contains(findings, f => f.Category == "Target Framework" && f.Severity == Severity.Warning);
    }

    [Fact]
    public async Task Does_not_flag_net9_target_framework()
    {
        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """;
        var findings = await Analyze(csproj);
        Assert.DoesNotContain(findings, f => f.Category == "Target Framework");
    }
}
