using MigrationScanner.Analyzers;
using MigrationScanner.Scanner;

namespace MigrationScanner.Tests.Analyzers;

public class NugetAnalyzerTests
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
            return (await new NugetAnalyzer().AnalyzeAsync(context)).ToList();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Flags_System_Management_as_error()
    {
        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="System.Management" Version="8.0.0" />
              </ItemGroup>
            </Project>
            """;
        var findings = await Analyze(csproj);
        Assert.Contains(findings, f => f.Severity == Severity.Error && f.Category == "Windows Package");
    }

    [Fact]
    public async Task Flags_Topshelf_as_warning()
    {
        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Topshelf" Version="4.3.0" />
              </ItemGroup>
            </Project>
            """;
        var findings = await Analyze(csproj);
        Assert.Contains(findings, f => f.Severity == Severity.Warning && f.Category == "Windows Package");
    }

    [Fact]
    public async Task Does_not_flag_cross_platform_package()
    {
        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """;
        var findings = await Analyze(csproj);
        Assert.Empty(findings);
    }
}
