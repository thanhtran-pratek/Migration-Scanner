# Migration Scanner Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a .NET 9.0 CLI tool that scans a .NET solution for Windows-specific issues and generates a self-contained HTML report.

**Architecture:** Hybrid approach — Roslyn AST for `.cs` files, regex/JSON parsing for config files, XML parsing for `.csproj` files. A `ScanOrchestrator` fans out to four `IAnalyzer` implementations, collects `Finding` objects, and passes them to `HtmlReporter`.

**Tech Stack:** .NET 9.0, Microsoft.CodeAnalysis.CSharp (Roslyn), System.CommandLine, System.Text.Json, System.Xml.Linq, xUnit

---

## File Map

| File | Responsibility |
|------|---------------|
| `MigrationScanner/MigrationScanner.csproj` | Main project with NuGet dependencies |
| `MigrationScanner/Program.cs` | CLI entry point, argument parsing, wires up orchestrator |
| `MigrationScanner/Scanner/Severity.cs` | `Severity` enum: Info / Warning / Error |
| `MigrationScanner/Scanner/Finding.cs` | Immutable finding record |
| `MigrationScanner/Scanner/ScanContext.cs` | File lists passed to analyzers |
| `MigrationScanner/Scanner/ScanOrchestrator.cs` | File discovery, runs analyzers, returns sorted findings |
| `MigrationScanner/Analyzers/IAnalyzer.cs` | `Task<IEnumerable<Finding>> AnalyzeAsync(ScanContext)` |
| `MigrationScanner/Analyzers/RoslynAnalyzer.cs` | Roslyn: P/Invoke, Windows namespaces, paths, hosting calls |
| `MigrationScanner/Analyzers/ConfigAnalyzer.cs` | Regex/JSON: secrets, hardcoded IPs, Windows paths in config |
| `MigrationScanner/Analyzers/ProjectAnalyzer.cs` | XML: Windows RID, WinForms/WPF, old TFMs |
| `MigrationScanner/Analyzers/NugetAnalyzer.cs` | PackageReference vs curated Windows-only list |
| `MigrationScanner/Rules/KnownWindowsPackages.cs` | Curated dict: package → (severity, alternative) |
| `MigrationScanner/Report/HtmlReporter.cs` | Self-contained HTML generation |
| `MigrationScanner.Tests/MigrationScanner.Tests.csproj` | xUnit test project |
| `MigrationScanner.Tests/Analyzers/RoslynAnalyzerTests.cs` | Unit tests for Roslyn rules |
| `MigrationScanner.Tests/Analyzers/ConfigAnalyzerTests.cs` | Unit tests for config scanning |
| `MigrationScanner.Tests/Analyzers/ProjectAnalyzerTests.cs` | Unit tests for .csproj scanning |
| `MigrationScanner.Tests/Analyzers/NugetAnalyzerTests.cs` | Unit tests for NuGet scanning |
| `MigrationScanner.Tests/Report/HtmlReporterTests.cs` | Unit tests for HTML output |

---

## Task 1: Scaffold the Solution

**Files:**
- Create: `MigrationScanner.sln`
- Create: `MigrationScanner/MigrationScanner.csproj`
- Create: `MigrationScanner.Tests/MigrationScanner.Tests.csproj`

- [ ] **Step 1: Create solution and projects**

```bash
cd C:\Users\TranVanThanh\Desktop\scan-project
dotnet new sln -n MigrationScanner
dotnet new console -n MigrationScanner -o MigrationScanner --framework net9.0
dotnet new xunit -n MigrationScanner.Tests -o MigrationScanner.Tests --framework net9.0
dotnet sln add MigrationScanner/MigrationScanner.csproj
dotnet sln add MigrationScanner.Tests/MigrationScanner.Tests.csproj
dotnet add MigrationScanner.Tests/MigrationScanner.Tests.csproj reference MigrationScanner/MigrationScanner.csproj
```

- [ ] **Step 2: Add NuGet packages to main project**

```bash
dotnet add MigrationScanner/MigrationScanner.csproj package Microsoft.CodeAnalysis.CSharp --version 4.11.0
dotnet add MigrationScanner/MigrationScanner.csproj package System.CommandLine --version 2.0.0-beta4.22272.1
```

- [ ] **Step 3: Replace `MigrationScanner.csproj` content**

Replace `MigrationScanner/MigrationScanner.csproj` with:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>MigrationScanner</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.11.0" />
    <PackageReference Include="System.CommandLine" Version="2.0.0-beta4.22272.1" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Verify build**

```bash
dotnet build MigrationScanner.sln
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git init
git add .
git commit -m "chore: scaffold MigrationScanner solution"
```

---

## Task 2: Core Models

**Files:**
- Create: `MigrationScanner/Scanner/Severity.cs`
- Create: `MigrationScanner/Scanner/Finding.cs`
- Create: `MigrationScanner/Scanner/ScanContext.cs`

- [ ] **Step 1: Create `Severity.cs`**

```csharp
// MigrationScanner/Scanner/Severity.cs
namespace MigrationScanner.Scanner;

public enum Severity { Info, Warning, Error }
```

- [ ] **Step 2: Create `Finding.cs`**

```csharp
// MigrationScanner/Scanner/Finding.cs
namespace MigrationScanner.Scanner;

public record Finding(
    string FilePath,
    int Line,
    Severity Severity,
    string Category,
    string Message,
    string Snippet,
    string? Suggestion = null
);
```

- [ ] **Step 3: Create `ScanContext.cs`**

```csharp
// MigrationScanner/Scanner/ScanContext.cs
namespace MigrationScanner.Scanner;

public record ScanContext(
    string RootPath,
    IReadOnlyList<string> CsFiles,
    IReadOnlyList<string> ConfigFiles,
    IReadOnlyList<string> ProjectFiles
);
```

- [ ] **Step 4: Write a failing test to verify model construction**

Create `MigrationScanner.Tests/Analyzers/ModelTests.cs`:
```csharp
using MigrationScanner.Scanner;

namespace MigrationScanner.Tests.Analyzers;

public class ModelTests
{
    [Fact]
    public void Finding_stores_all_properties()
    {
        var f = new Finding("foo.cs", 42, Severity.Error, "P/Invoke", "msg", "snippet", "fix");
        Assert.Equal("foo.cs", f.FilePath);
        Assert.Equal(42, f.Line);
        Assert.Equal(Severity.Error, f.Severity);
        Assert.Equal("P/Invoke", f.Category);
        Assert.Equal("msg", f.Message);
        Assert.Equal("snippet", f.Snippet);
        Assert.Equal("fix", f.Suggestion);
    }

    [Fact]
    public void Finding_suggestion_defaults_to_null()
    {
        var f = new Finding("foo.cs", 1, Severity.Info, "cat", "msg", "snip");
        Assert.Null(f.Suggestion);
    }
}
```

- [ ] **Step 5: Run tests**

```bash
dotnet test MigrationScanner.Tests/MigrationScanner.Tests.csproj
```
Expected: `Passed: 2`

- [ ] **Step 6: Commit**

```bash
git add MigrationScanner/Scanner/ MigrationScanner.Tests/Analyzers/ModelTests.cs
git commit -m "feat: add core models Finding, Severity, ScanContext"
```

---

## Task 3: IAnalyzer Interface and ScanOrchestrator

**Files:**
- Create: `MigrationScanner/Analyzers/IAnalyzer.cs`
- Create: `MigrationScanner/Scanner/ScanOrchestrator.cs`
- Create: `MigrationScanner.Tests/Analyzers/ScanOrchestratorTests.cs`

- [ ] **Step 1: Create `IAnalyzer.cs`**

```csharp
// MigrationScanner/Analyzers/IAnalyzer.cs
using MigrationScanner.Scanner;

namespace MigrationScanner.Analyzers;

public interface IAnalyzer
{
    Task<IEnumerable<Finding>> AnalyzeAsync(ScanContext context);
}
```

- [ ] **Step 2: Write failing test for ScanOrchestrator**

Create `MigrationScanner.Tests/Analyzers/ScanOrchestratorTests.cs`:
```csharp
using MigrationScanner.Analyzers;
using MigrationScanner.Scanner;

namespace MigrationScanner.Tests.Analyzers;

public class ScanOrchestratorTests
{
    [Fact]
    public async Task ScanAsync_aggregates_findings_from_all_analyzers()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "foo.cs"), "// empty");

        var a1 = new StubAnalyzer(new Finding("a.cs", 1, Severity.Error, "cat", "msg", "snip"));
        var a2 = new StubAnalyzer(new Finding("b.cs", 2, Severity.Warning, "cat", "msg", "snip"));
        var orchestrator = new ScanOrchestrator(new IAnalyzer[] { a1, a2 });

        var findings = (await orchestrator.ScanAsync(dir)).ToList();

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.FilePath == "a.cs");
        Assert.Contains(findings, f => f.FilePath == "b.cs");

        Directory.Delete(dir, recursive: true);
    }

    private class StubAnalyzer : IAnalyzer
    {
        private readonly Finding _finding;
        public StubAnalyzer(Finding finding) => _finding = finding;
        public Task<IEnumerable<Finding>> AnalyzeAsync(ScanContext context)
            => Task.FromResult<IEnumerable<Finding>>(new[] { _finding });
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

```bash
dotnet test MigrationScanner.Tests/MigrationScanner.Tests.csproj --filter "ScanOrchestratorTests"
```
Expected: `Error` — `ScanOrchestrator` not found.

- [ ] **Step 4: Create `ScanOrchestrator.cs`**

```csharp
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

        var csFiles = Directory
            .GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();

        var configFiles = Directory
            .GetFiles(root, "appsettings*.json", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(root, "*.config", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();

        var projectFiles = Directory
            .GetFiles(root, "*.csproj", SearchOption.AllDirectories)
            .ToList();

        return new ScanContext(root, csFiles, configFiles, projectFiles);
    }
}
```

- [ ] **Step 5: Run tests**

```bash
dotnet test MigrationScanner.Tests/MigrationScanner.Tests.csproj
```
Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add MigrationScanner/Analyzers/IAnalyzer.cs MigrationScanner/Scanner/ScanOrchestrator.cs MigrationScanner.Tests/Analyzers/ScanOrchestratorTests.cs
git commit -m "feat: add IAnalyzer interface and ScanOrchestrator"
```

---

## Task 4: RoslynAnalyzer — P/Invoke and Windows Namespaces

**Files:**
- Create: `MigrationScanner/Analyzers/RoslynAnalyzer.cs`
- Create: `MigrationScanner.Tests/Analyzers/RoslynAnalyzerTests.cs`

- [ ] **Step 1: Write failing tests for P/Invoke and Windows namespace detection**

Create `MigrationScanner.Tests/Analyzers/RoslynAnalyzerTests.cs`:
```csharp
using MigrationScanner.Analyzers;
using MigrationScanner.Scanner;

namespace MigrationScanner.Tests.Analyzers;

public class RoslynAnalyzerTests
{
    private static async Task<List<Finding>> Analyze(string code)
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "Test.cs");
        await File.WriteAllTextAsync(file, code);
        var context = new ScanContext(dir, new[] { file }, Array.Empty<string>(), Array.Empty<string>());
        var findings = (await new RoslynAnalyzer().AnalyzeAsync(context)).ToList();
        Directory.Delete(dir, recursive: true);
        return findings;
    }

    [Fact]
    public async Task Detects_DllImport_to_windows_dll()
    {
        var code = """
            using System.Runtime.InteropServices;
            class C {
                [DllImport("kernel32.dll")]
                static extern IntPtr GetConsoleWindow();
            }
            """;
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Severity == Severity.Error && f.Category == "P/Invoke");
    }

    [Fact]
    public async Task Detects_windows_only_namespace_using()
    {
        var code = "using Microsoft.Win32;";
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Severity == Severity.Error && f.Category == "Windows Namespace");
    }

    [Fact]
    public async Task Detects_System_Management_namespace()
    {
        var code = "using System.Management;";
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Category == "Windows Namespace");
    }

    [Fact]
    public async Task Does_not_flag_cross_platform_namespaces()
    {
        var code = "using System.Collections.Generic;";
        var findings = await Analyze(code);
        Assert.Empty(findings);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test MigrationScanner.Tests/MigrationScanner.Tests.csproj --filter "RoslynAnalyzerTests"
```
Expected: `Error` — `RoslynAnalyzer` not found.

- [ ] **Step 3: Create `RoslynAnalyzer.cs` (P/Invoke + namespace detection)**

```csharp
// MigrationScanner/Analyzers/RoslynAnalyzer.cs
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MigrationScanner.Scanner;

namespace MigrationScanner.Analyzers;

public class RoslynAnalyzer : IAnalyzer
{
    private static readonly HashSet<string> WindowsDlls = new(StringComparer.OrdinalIgnoreCase)
    {
        "kernel32.dll", "user32.dll", "advapi32.dll", "ntdll.dll",
        "gdi32.dll", "shell32.dll", "ole32.dll", "winmm.dll", "winspool.drv"
    };

    private static readonly HashSet<string> WindowsNamespacePrefixes = new()
    {
        "Microsoft.Win32",
        "System.Management",
        "System.ServiceProcess",
        "System.Windows.Forms",
        "System.Drawing"
    };

    private static readonly HashSet<string> WindowsHostingMethods = new()
    {
        "UseIISIntegration", "UseWindowsService"
    };

    public async Task<IEnumerable<Finding>> AnalyzeAsync(ScanContext context)
    {
        var findings = new List<Finding>();
        foreach (var file in context.CsFiles)
        {
            var source = await File.ReadAllTextAsync(file);
            var tree = CSharpSyntaxTree.ParseText(source);
            var root = await tree.GetRootAsync();
            var lines = source.Split('\n');
            findings.AddRange(AnalyzeTree(file, lines, root));
        }
        return findings;
    }

    private IEnumerable<Finding> AnalyzeTree(string filePath, string[] lines, SyntaxNode root)
    {
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case UsingDirectiveSyntax u:
                    foreach (var f in CheckUsingDirective(filePath, lines, u)) yield return f;
                    break;
                case AttributeSyntax a:
                    foreach (var f in CheckAttribute(filePath, lines, a)) yield return f;
                    break;
                case LiteralExpressionSyntax l when l.IsKind(SyntaxKind.StringLiteralExpression):
                    foreach (var f in CheckStringLiteral(filePath, lines, l)) yield return f;
                    break;
                case InvocationExpressionSyntax i:
                    foreach (var f in CheckInvocation(filePath, lines, i)) yield return f;
                    break;
            }
        }
    }

    private IEnumerable<Finding> CheckUsingDirective(string filePath, string[] lines, UsingDirectiveSyntax node)
    {
        var ns = node.Name?.ToString() ?? "";
        if (WindowsNamespacePrefixes.Any(p => ns == p || ns.StartsWith(p + ".")))
        {
            var line = GetLine(node);
            yield return new Finding(filePath, line, Severity.Error, "Windows Namespace",
                $"Windows-specific namespace: {ns}",
                GetSnippet(lines, line - 1),
                "Remove or replace with a cross-platform alternative");
        }
    }

    private IEnumerable<Finding> CheckAttribute(string filePath, string[] lines, AttributeSyntax node)
    {
        var name = node.Name.ToString();
        if (name != "DllImport" && name != "System.Runtime.InteropServices.DllImport")
            yield break;

        var dllArg = node.ArgumentList?.Arguments.FirstOrDefault()?.ToString().Trim('"');
        if (dllArg != null && WindowsDlls.Contains(dllArg))
        {
            var line = GetLine(node);
            yield return new Finding(filePath, line, Severity.Error, "P/Invoke",
                $"P/Invoke to Windows-only DLL: {dllArg}",
                GetSnippet(lines, line - 1),
                "Remove P/Invoke; use cross-platform .NET APIs instead");
        }
    }

    private IEnumerable<Finding> CheckStringLiteral(string filePath, string[] lines, LiteralExpressionSyntax node)
    {
        var value = node.Token.ValueText;
        if (System.Text.RegularExpressions.Regex.IsMatch(value, @"^[A-Za-z]:\\|^\\\\[a-zA-Z]"))
        {
            var line = GetLine(node);
            yield return new Finding(filePath, line, Severity.Warning, "Hardcoded Path",
                $"Hardcoded Windows path in string literal: {value}",
                GetSnippet(lines, line - 1),
                "Use Path.Combine, IConfiguration, or environment variables");
        }
    }

    private IEnumerable<Finding> CheckInvocation(string filePath, string[] lines, InvocationExpressionSyntax node)
    {
        var methodName = node.Expression switch
        {
            MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
            IdentifierNameSyntax i => i.Identifier.Text,
            _ => null
        };

        if (methodName != null && WindowsHostingMethods.Contains(methodName))
        {
            var line = GetLine(node);
            yield return new Finding(filePath, line, Severity.Warning, "Windows Hosting",
                $"Windows-specific hosting call: {methodName}()",
                GetSnippet(lines, line - 1),
                "Replace with Linux-compatible hosting (Kestrel, IHostedService)");
        }
    }

    private static int GetLine(SyntaxNode node) =>
        node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    private static string GetSnippet(string[] lines, int zeroBasedLine)
    {
        var start = Math.Max(0, zeroBasedLine - 1);
        var end = Math.Min(lines.Length - 1, zeroBasedLine + 1);
        return string.Join('\n', lines[start..(end + 1)]);
    }
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test MigrationScanner.Tests/MigrationScanner.Tests.csproj --filter "RoslynAnalyzerTests"
```
Expected: All 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add MigrationScanner/Analyzers/RoslynAnalyzer.cs MigrationScanner.Tests/Analyzers/RoslynAnalyzerTests.cs
git commit -m "feat: RoslynAnalyzer - P/Invoke and Windows namespace detection"
```

---

## Task 5: RoslynAnalyzer — Windows Paths, Hosting, Timezones

**Files:**
- Modify: `MigrationScanner/Analyzers/RoslynAnalyzer.cs` — add timezone + locale detection
- Modify: `MigrationScanner.Tests/Analyzers/RoslynAnalyzerTests.cs` — add coverage tests

- [ ] **Step 1: Add Windows timezone ID detection to `RoslynAnalyzer.cs`**

Add the following static field and detection logic to `RoslynAnalyzer.cs`.

Add to the static fields section (after `WindowsHostingMethods`):
```csharp
    private static readonly HashSet<string> WindowsTimezoneIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Eastern Standard Time", "Central Standard Time", "Mountain Standard Time",
        "Pacific Standard Time", "UTC", "GMT Standard Time", "Romance Standard Time",
        "W. Europe Standard Time", "Central Europe Standard Time", "SE Asia Standard Time"
    };
```

Replace the `CheckInvocation` method with:
```csharp
    private IEnumerable<Finding> CheckInvocation(string filePath, string[] lines, InvocationExpressionSyntax node)
    {
        var methodName = node.Expression switch
        {
            MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
            IdentifierNameSyntax i => i.Identifier.Text,
            _ => null
        };

        if (methodName != null && WindowsHostingMethods.Contains(methodName))
        {
            var line = GetLine(node);
            yield return new Finding(filePath, line, Severity.Warning, "Windows Hosting",
                $"Windows-specific hosting call: {methodName}()",
                GetSnippet(lines, line - 1),
                "Replace with Linux-compatible hosting (Kestrel, IHostedService)");
        }

        if (methodName == "FindSystemTimeZoneById")
        {
            var arg = node.ArgumentList.Arguments.FirstOrDefault()?.Expression;
            if (arg is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
            {
                var tzId = lit.Token.ValueText;
                if (WindowsTimezoneIds.Contains(tzId))
                {
                    var line = GetLine(node);
                    yield return new Finding(filePath, line, Severity.Warning, "Windows Timezone",
                        $"Windows timezone ID \"{tzId}\" may not exist on Linux",
                        GetSnippet(lines, line - 1),
                        "Use IANA timezone IDs (e.g. \"America/New_York\") and TimeZoneConverter NuGet package");
                }
            }
        }
    }
```

- [ ] **Step 2: Add tests for hosting, paths, and timezone detection**

Append to `MigrationScanner.Tests/Analyzers/RoslynAnalyzerTests.cs`:
```csharp
    [Fact]
    public async Task Detects_UseIISIntegration_call()
    {
        var code = """
            class C {
                void M(IWebHostBuilder b) => b.UseIISIntegration();
            }
            """;
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Category == "Windows Hosting" && f.Severity == Severity.Warning);
    }

    [Fact]
    public async Task Detects_UseWindowsService_call()
    {
        var code = """
            class C {
                void M(IHostBuilder b) => b.UseWindowsService();
            }
            """;
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Category == "Windows Hosting");
    }

    [Fact]
    public async Task Detects_hardcoded_windows_path_in_string_literal()
    {
        var code = """
            class C {
                string path = @"C:\logs\app.log";
            }
            """;
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Category == "Hardcoded Path" && f.Severity == Severity.Warning);
    }

    [Fact]
    public async Task Does_not_flag_relative_paths()
    {
        var code = """
            class C {
                string path = "logs/app.log";
            }
            """;
        var findings = await Analyze(code);
        Assert.DoesNotContain(findings, f => f.Category == "Hardcoded Path");
    }

    [Fact]
    public async Task Detects_windows_timezone_id()
    {
        var code = """
            using System;
            class C {
                void M() {
                    var tz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                }
            }
            """;
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Category == "Windows Timezone" && f.Severity == Severity.Warning);
    }

    [Fact]
    public async Task Does_not_flag_iana_timezone_id()
    {
        var code = """
            using System;
            class C {
                void M() {
                    var tz = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
                }
            }
            """;
        var findings = await Analyze(code);
        Assert.DoesNotContain(findings, f => f.Category == "Windows Timezone");
    }
```

- [ ] **Step 3: Run tests**

```bash
dotnet test MigrationScanner.Tests/MigrationScanner.Tests.csproj --filter "RoslynAnalyzerTests"
```
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add MigrationScanner/Analyzers/RoslynAnalyzer.cs MigrationScanner.Tests/Analyzers/RoslynAnalyzerTests.cs
git commit -m "feat: RoslynAnalyzer - add Windows timezone and hosting detection"
```

---

## Task 6: ConfigAnalyzer

**Files:**
- Create: `MigrationScanner/Analyzers/ConfigAnalyzer.cs`
- Create: `MigrationScanner.Tests/Analyzers/ConfigAnalyzerTests.cs`

- [ ] **Step 1: Write failing tests**

Create `MigrationScanner.Tests/Analyzers/ConfigAnalyzerTests.cs`:
```csharp
using MigrationScanner.Analyzers;
using MigrationScanner.Scanner;

namespace MigrationScanner.Tests.Analyzers;

public class ConfigAnalyzerTests
{
    private static async Task<List<Finding>> Analyze(string filename, string content)
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, filename);
        await File.WriteAllTextAsync(file, content);
        var context = new ScanContext(dir, Array.Empty<string>(), new[] { file }, Array.Empty<string>());
        var findings = (await new ConfigAnalyzer().AnalyzeAsync(context)).ToList();
        Directory.Delete(dir, recursive: true);
        return findings;
    }

    [Fact]
    public async Task Detects_plaintext_password_in_connection_string()
    {
        var json = """
            {
              "ConnectionStrings": {
                "Default": "Server=myserver;Database=mydb;User Id=sa;Password=secret123"
              }
            }
            """;
        var findings = await Analyze("appsettings.json", json);
        Assert.Contains(findings, f => f.Category == "Hardcoded Secret" && f.Severity == Severity.Error);
    }

    [Fact]
    public async Task Detects_windows_path_in_config_value()
    {
        var json = """
            {
              "Logging": {
                "LogPath": "C:\\logs\\app.log"
              }
            }
            """;
        var findings = await Analyze("appsettings.json", json);
        Assert.Contains(findings, f => f.Category == "Hardcoded Path" && f.Severity == Severity.Warning);
    }

    [Fact]
    public async Task Detects_hardcoded_ip_address()
    {
        var json = """
            {
              "Redis": {
                "Host": "192.168.1.100"
              }
            }
            """;
        var findings = await Analyze("appsettings.json", json);
        Assert.Contains(findings, f => f.Category == "Hardcoded IP" && f.Severity == Severity.Warning);
    }

    [Fact]
    public async Task Does_not_flag_localhost()
    {
        var json = """
            { "Host": "localhost" }
            """;
        var findings = await Analyze("appsettings.json", json);
        Assert.DoesNotContain(findings, f => f.Category == "Hardcoded IP");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test MigrationScanner.Tests/MigrationScanner.Tests.csproj --filter "ConfigAnalyzerTests"
```
Expected: `Error` — `ConfigAnalyzer` not found.

- [ ] **Step 3: Create `ConfigAnalyzer.cs`**

```csharp
// MigrationScanner/Analyzers/ConfigAnalyzer.cs
using System.Text.Json;
using System.Text.RegularExpressions;
using MigrationScanner.Scanner;

namespace MigrationScanner.Analyzers;

public class ConfigAnalyzer : IAnalyzer
{
    private static readonly Regex WindowsPathRegex = new(@"[A-Za-z]:\\|\\\\[a-zA-Z]", RegexOptions.Compiled);
    private static readonly Regex IpAddressRegex = new(@"^(\d{1,3}\.){3}\d{1,3}$", RegexOptions.Compiled);
    private static readonly Regex PasswordInConnStrRegex = new(@"[Pp]assword\s*=\s*[^;'""\s]{3,}", RegexOptions.Compiled);
    private static readonly Regex PwdInConnStrRegex = new(@"\bpwd\s*=\s*[^;'""\s]{3,}", RegexOptions.Compiled);

    public async Task<IEnumerable<Finding>> AnalyzeAsync(ScanContext context)
    {
        var findings = new List<Finding>();
        foreach (var file in context.ConfigFiles)
        {
            if (!file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            var content = await File.ReadAllTextAsync(file);
            findings.AddRange(AnalyzeJson(file, content));
        }
        return findings;
    }

    private IEnumerable<Finding> AnalyzeJson(string filePath, string content)
    {
        var lines = content.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineNum = i + 1;
            var snippet = GetSnippet(lines, i);

            if (PasswordInConnStrRegex.IsMatch(line) || PwdInConnStrRegex.IsMatch(line))
                yield return new Finding(filePath, lineNum, Severity.Error, "Hardcoded Secret",
                    "Plaintext password found in connection string",
                    snippet,
                    "Move credentials to environment variables or AWS Secrets Manager");

            var valueMatch = Regex.Match(line, @""":\s*""([^""]+)""");
            if (valueMatch.Success)
            {
                var value = valueMatch.Groups[1].Value;

                if (WindowsPathRegex.IsMatch(value))
                    yield return new Finding(filePath, lineNum, Severity.Warning, "Hardcoded Path",
                        $"Windows path in config value: {value}",
                        snippet,
                        "Use environment variables or relative paths");

                if (IpAddressRegex.IsMatch(value))
                    yield return new Finding(filePath, lineNum, Severity.Warning, "Hardcoded IP",
                        $"Hardcoded IP address: {value}",
                        snippet,
                        "Use DNS names or service discovery instead of hardcoded IPs");
            }
        }
    }

    private static string GetSnippet(string[] lines, int zeroBasedLine)
    {
        var start = Math.Max(0, zeroBasedLine - 1);
        var end = Math.Min(lines.Length - 1, zeroBasedLine + 1);
        return string.Join('\n', lines[start..(end + 1)]);
    }
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test MigrationScanner.Tests/MigrationScanner.Tests.csproj --filter "ConfigAnalyzerTests"
```
Expected: All 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add MigrationScanner/Analyzers/ConfigAnalyzer.cs MigrationScanner.Tests/Analyzers/ConfigAnalyzerTests.cs
git commit -m "feat: ConfigAnalyzer - secrets, Windows paths, hardcoded IPs in JSON config"
```

---

## Task 7: ProjectAnalyzer

**Files:**
- Create: `MigrationScanner/Analyzers/ProjectAnalyzer.cs`
- Create: `MigrationScanner.Tests/Analyzers/ProjectAnalyzerTests.cs`

- [ ] **Step 1: Write failing tests**

Create `MigrationScanner.Tests/Analyzers/ProjectAnalyzerTests.cs`:
```csharp
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
        var findings = (await new ProjectAnalyzer().AnalyzeAsync(context)).ToList();
        Directory.Delete(dir, recursive: true);
        return findings;
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
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test MigrationScanner.Tests/MigrationScanner.Tests.csproj --filter "ProjectAnalyzerTests"
```
Expected: `Error` — `ProjectAnalyzer` not found.

- [ ] **Step 3: Create `ProjectAnalyzer.cs`**

```csharp
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
            var content = await File.ReadAllTextAsync(file);
            findings.AddRange(AnalyzeProject(file, content));
        }
        return findings;
    }

    private IEnumerable<Finding> AnalyzeProject(string filePath, string content)
    {
        XDocument doc;
        try { doc = XDocument.Parse(content); }
        catch { yield break; }

        foreach (var el in doc.Descendants())
        {
            var name = el.Name.LocalName;
            var value = el.Value.Trim();

            if (name == "RuntimeIdentifier" && value.StartsWith("win", StringComparison.OrdinalIgnoreCase))
                yield return new Finding(filePath, 0, Severity.Error, "Windows RID",
                    $"Windows-only RuntimeIdentifier: {value}",
                    $"<RuntimeIdentifier>{value}</RuntimeIdentifier>",
                    "Remove or use linux-x64 / linux-arm64");

            if ((name == "UseWindowsForms" || name == "UseWPF") && value.Equals("true", StringComparison.OrdinalIgnoreCase))
                yield return new Finding(filePath, 0, Severity.Error, "WinForms/WPF",
                    $"{name} is not supported on Linux",
                    $"<{name}>true</{name}>",
                    "WinForms/WPF cannot run on Linux — consider a web-based UI");

            if (name == "TargetFramework" && OldFrameworks.Contains(value))
                yield return new Finding(filePath, 0, Severity.Warning, "Target Framework",
                    $"Old target framework: {value}",
                    $"<TargetFramework>{value}</TargetFramework>",
                    "Upgrade to net9.0 for full Linux/Docker compatibility");

            if (name == "PlatformTarget" && value.Equals("x86", StringComparison.OrdinalIgnoreCase))
                yield return new Finding(filePath, 0, Severity.Warning, "Platform Target",
                    "x86 PlatformTarget may cause issues on Linux/Docker",
                    $"<PlatformTarget>{value}</PlatformTarget>",
                    "Change to AnyCPU or x64");
        }
    }
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test MigrationScanner.Tests/MigrationScanner.Tests.csproj --filter "ProjectAnalyzerTests"
```
Expected: All 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add MigrationScanner/Analyzers/ProjectAnalyzer.cs MigrationScanner.Tests/Analyzers/ProjectAnalyzerTests.cs
git commit -m "feat: ProjectAnalyzer - Windows RID, WinForms/WPF, old TFMs"
```

---

## Task 8: NugetAnalyzer + KnownWindowsPackages

**Files:**
- Create: `MigrationScanner/Rules/KnownWindowsPackages.cs`
- Create: `MigrationScanner/Analyzers/NugetAnalyzer.cs`
- Create: `MigrationScanner.Tests/Analyzers/NugetAnalyzerTests.cs`

- [ ] **Step 1: Create `KnownWindowsPackages.cs`**

```csharp
// MigrationScanner/Rules/KnownWindowsPackages.cs
using MigrationScanner.Scanner;

namespace MigrationScanner.Rules;

public static class KnownWindowsPackages
{
    public record PackageInfo(Severity Severity, string Reason, string? Alternative);

    public static readonly IReadOnlyDictionary<string, PackageInfo> All =
        new Dictionary<string, PackageInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["System.Management"] = new(Severity.Error,
                "WMI — not supported on Linux",
                null),
            ["Microsoft.Win32.Registry"] = new(Severity.Error,
                "Windows Registry — not available on Linux",
                "Use IConfiguration or environment variables"),
            ["System.DirectoryServices"] = new(Severity.Error,
                "Active Directory — not supported on Linux without workarounds",
                "Use LDAP library or Azure AD SDK"),
            ["Topshelf"] = new(Severity.Warning,
                "Windows service host — not cross-platform",
                "Use IHostedService / BackgroundService"),
            ["Nancy"] = new(Severity.Warning,
                "Unmaintained, Windows-biased web framework",
                "Migrate to ASP.NET Core minimal APIs"),
            ["Microsoft.Diagnostics.Runtime"] = new(Severity.Warning,
                "Limited Linux support",
                null),
            ["log4net"] = new(Severity.Info,
                "Some appenders are Windows-only",
                "Consider Serilog or Microsoft.Extensions.Logging"),
        };
}
```

- [ ] **Step 2: Write failing tests**

Create `MigrationScanner.Tests/Analyzers/NugetAnalyzerTests.cs`:
```csharp
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
        var findings = (await new NugetAnalyzer().AnalyzeAsync(context)).ToList();
        Directory.Delete(dir, recursive: true);
        return findings;
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
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
dotnet test MigrationScanner.Tests/MigrationScanner.Tests.csproj --filter "NugetAnalyzerTests"
```
Expected: `Error` — `NugetAnalyzer` not found.

- [ ] **Step 4: Create `NugetAnalyzer.cs`**

```csharp
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
            var content = await File.ReadAllTextAsync(file);
            findings.AddRange(AnalyzeProject(file, content));
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
```

- [ ] **Step 5: Run tests**

```bash
dotnet test MigrationScanner.Tests/MigrationScanner.Tests.csproj --filter "NugetAnalyzerTests"
```
Expected: All 3 tests pass.

- [ ] **Step 6: Commit**

```bash
git add MigrationScanner/Rules/KnownWindowsPackages.cs MigrationScanner/Analyzers/NugetAnalyzer.cs MigrationScanner.Tests/Analyzers/NugetAnalyzerTests.cs
git commit -m "feat: NugetAnalyzer + KnownWindowsPackages curated list"
```

---

## Task 9: HtmlReporter

**Files:**
- Create: `MigrationScanner/Report/HtmlReporter.cs`
- Create: `MigrationScanner.Tests/Report/HtmlReporterTests.cs`

- [ ] **Step 1: Write failing tests**

Create `MigrationScanner.Tests/Report/HtmlReporterTests.cs`:
```csharp
using MigrationScanner.Report;
using MigrationScanner.Scanner;

namespace MigrationScanner.Tests.Report;

public class HtmlReporterTests
{
    private static List<Finding> SampleFindings() => new()
    {
        new Finding("src/Foo.cs", 10, Severity.Error, "P/Invoke", "DllImport kernel32", "code", "fix"),
        new Finding("appsettings.json", 5, Severity.Warning, "Hardcoded Path", "C:\\logs", "json", "use env vars"),
        new Finding("MyApp.csproj", 0, Severity.Info, "Windows Package", "log4net", "xml", null),
    };

    [Fact]
    public void Generate_produces_html_string()
    {
        var html = HtmlReporter.Generate(SampleFindings(), "MySolution.sln");
        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("<html", html);
    }

    [Fact]
    public void Generate_includes_finding_messages()
    {
        var html = HtmlReporter.Generate(SampleFindings(), "MySolution.sln");
        Assert.Contains("DllImport kernel32", html);
        Assert.Contains("Hardcoded Path", html);
    }

    [Fact]
    public void Generate_includes_severity_counts()
    {
        var html = HtmlReporter.Generate(SampleFindings(), "MySolution.sln");
        Assert.Contains("1", html); // at least "1 Error"
    }

    [Fact]
    public void Generate_includes_solution_name()
    {
        var html = HtmlReporter.Generate(SampleFindings(), "MySolution.sln");
        Assert.Contains("MySolution.sln", html);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test MigrationScanner.Tests/MigrationScanner.Tests.csproj --filter "HtmlReporterTests"
```
Expected: `Error` — `HtmlReporter` not found.

- [ ] **Step 3: Create `HtmlReporter.cs`**

```csharp
// MigrationScanner/Report/HtmlReporter.cs
using MigrationScanner.Scanner;

namespace MigrationScanner.Report;

public static class HtmlReporter
{
    public static string Generate(IEnumerable<Finding> findings, string solutionName)
    {
        var list = findings.ToList();
        var errors = list.Count(f => f.Severity == Severity.Error);
        var warnings = list.Count(f => f.Severity == Severity.Warning);
        var infos = list.Count(f => f.Severity == Severity.Info);
        var date = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        var findingCards = string.Join('\n', list.Select(f =>
        {
            var badgeColor = f.Severity switch
            {
                Severity.Error => "#dc3545",
                Severity.Warning => "#fd7e14",
                _ => "#0dcaf0"
            };
            var suggestion = f.Suggestion != null
                ? $"<div class='suggestion'>💡 {Escape(f.Suggestion)}</div>"
                : "";
            return $"""
                <div class='finding {f.Severity.ToString().ToLower()}' data-severity='{f.Severity.ToString().ToLower()}'>
                  <div class='finding-header'>
                    <span class='badge' style='background:{badgeColor}'>{f.Severity}</span>
                    <span class='category'>{Escape(f.Category)}</span>
                    <span class='location'>{Escape(f.FilePath)}:{f.Line}</span>
                  </div>
                  <div class='message'>{Escape(f.Message)}</div>
                  <pre class='snippet'>{Escape(f.Snippet)}</pre>
                  {suggestion}
                </div>
                """;
        }));

        return $"""
            <!DOCTYPE html>
            <html lang='en'>
            <head>
            <meta charset='UTF-8'>
            <meta name='viewport' content='width=device-width, initial-scale=1.0'>
            <title>Migration Readiness Report</title>
            <style>
              body {{ font-family: system-ui, sans-serif; margin: 0; padding: 0; background: #f8f9fa; color: #212529; }}
              .header {{ background: #1e1e2e; color: white; padding: 24px 32px; }}
              .header h1 {{ margin: 0 0 4px; font-size: 1.6rem; }}
              .header .meta {{ color: #adb5bd; font-size: 0.9rem; }}
              .summary {{ display: flex; gap: 16px; padding: 20px 32px; background: white; border-bottom: 1px solid #dee2e6; }}
              .stat {{ padding: 12px 20px; border-radius: 8px; text-align: center; min-width: 80px; }}
              .stat.error {{ background: #f8d7da; color: #842029; }}
              .stat.warning {{ background: #fff3cd; color: #664d03; }}
              .stat.info {{ background: #cff4fc; color: #055160; }}
              .stat .count {{ font-size: 2rem; font-weight: bold; }}
              .stat .label {{ font-size: 0.8rem; text-transform: uppercase; }}
              .controls {{ padding: 16px 32px; background: white; border-bottom: 1px solid #dee2e6; }}
              .controls button {{ margin-right: 8px; padding: 6px 16px; border: 1px solid #dee2e6; border-radius: 4px; cursor: pointer; background: white; }}
              .controls button.active {{ background: #0d6efd; color: white; border-color: #0d6efd; }}
              .findings {{ padding: 20px 32px; max-width: 1200px; margin: 0 auto; }}
              .finding {{ background: white; border-radius: 8px; padding: 16px; margin-bottom: 12px; border-left: 4px solid #dee2e6; box-shadow: 0 1px 3px rgba(0,0,0,.08); }}
              .finding.error {{ border-left-color: #dc3545; }}
              .finding.warning {{ border-left-color: #fd7e14; }}
              .finding.info {{ border-left-color: #0dcaf0; }}
              .finding-header {{ display: flex; align-items: center; gap: 10px; margin-bottom: 8px; flex-wrap: wrap; }}
              .badge {{ padding: 2px 10px; border-radius: 12px; color: white; font-size: 0.75rem; font-weight: 600; text-transform: uppercase; }}
              .category {{ font-weight: 600; font-size: 0.9rem; }}
              .location {{ color: #6c757d; font-size: 0.85rem; margin-left: auto; font-family: monospace; }}
              .message {{ margin-bottom: 8px; }}
              .snippet {{ background: #1e1e2e; color: #cdd6f4; padding: 10px; border-radius: 4px; font-size: 0.82rem; overflow-x: auto; margin: 8px 0; }}
              .suggestion {{ color: #0f5132; background: #d1e7dd; padding: 6px 12px; border-radius: 4px; font-size: 0.85rem; }}
              .finding.hidden {{ display: none; }}
            </style>
            </head>
            <body>
            <div class='header'>
              <h1>Migration Readiness Report</h1>
              <div class='meta'>Scanned: {Escape(solutionName)} &nbsp;|&nbsp; Generated: {date}</div>
            </div>
            <div class='summary'>
              <div class='stat error'><div class='count'>{errors}</div><div class='label'>Errors</div></div>
              <div class='stat warning'><div class='count'>{warnings}</div><div class='label'>Warnings</div></div>
              <div class='stat info'><div class='count'>{infos}</div><div class='label'>Info</div></div>
            </div>
            <div class='controls'>
              <strong>Filter: </strong>
              <button class='active' onclick='filter("all")'>All ({list.Count})</button>
              <button onclick='filter("error")'>Errors ({errors})</button>
              <button onclick='filter("warning")'>Warnings ({warnings})</button>
              <button onclick='filter("info")'>Info ({infos})</button>
            </div>
            <div class='findings'>
              {findingCards}
            </div>
            <script>
            function filter(sev) {{
              document.querySelectorAll('.controls button').forEach(b => b.classList.remove('active'));
              event.target.classList.add('active');
              document.querySelectorAll('.finding').forEach(el => {{
                el.classList.toggle('hidden', sev !== 'all' && el.dataset.severity !== sev);
              }});
            }}
            </script>
            </body>
            </html>
            """;
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test MigrationScanner.Tests/MigrationScanner.Tests.csproj --filter "HtmlReporterTests"
```
Expected: All 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add MigrationScanner/Report/HtmlReporter.cs MigrationScanner.Tests/Report/HtmlReporterTests.cs
git commit -m "feat: HtmlReporter - self-contained HTML report with severity filtering"
```

---

## Task 10: CLI Entry Point

**Files:**
- Modify: `MigrationScanner/Program.cs`

- [ ] **Step 1: Replace `Program.cs` with CLI wiring**

```csharp
// MigrationScanner/Program.cs
using System.CommandLine;
using MigrationScanner.Analyzers;
using MigrationScanner.Report;
using MigrationScanner.Scanner;

var pathOption = new Option<string>(
    name: "--path",
    description: "Path to the .sln file or root folder of the solution")
{ IsRequired = true };

var outputOption = new Option<string>(
    name: "--output",
    description: "Output path for the HTML report",
    getDefaultValue: () => "migration-report.html");

var rootCommand = new RootCommand("Scans a .NET solution for Windows-to-Linux migration issues");
rootCommand.AddOption(pathOption);
rootCommand.AddOption(outputOption);

rootCommand.SetHandler(async (path, output) =>
{
    Console.WriteLine($"Scanning: {path}");

    var orchestrator = new ScanOrchestrator(new IAnalyzer[]
    {
        new RoslynAnalyzer(),
        new ConfigAnalyzer(),
        new ProjectAnalyzer(),
        new NugetAnalyzer(),
    });

    var findings = (await orchestrator.ScanAsync(path)).ToList();

    Console.WriteLine($"Found {findings.Count} issue(s): " +
        $"{findings.Count(f => f.Severity == Severity.Error)} errors, " +
        $"{findings.Count(f => f.Severity == Severity.Warning)} warnings, " +
        $"{findings.Count(f => f.Severity == Severity.Info)} info");

    var solutionName = Path.GetFileName(path.TrimEnd('/', '\\'));
    var html = HtmlReporter.Generate(findings, solutionName);
    await File.WriteAllTextAsync(output, html);

    Console.WriteLine($"Report written to: {Path.GetFullPath(output)}");
}, pathOption, outputOption);

return await rootCommand.InvokeAsync(args);
```

- [ ] **Step 2: Verify build**

```bash
dotnet build MigrationScanner.sln
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Run all tests**

```bash
dotnet test MigrationScanner.sln
```
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add MigrationScanner/Program.cs
git commit -m "feat: CLI entry point wiring all analyzers and HtmlReporter"
```

---

## Task 11: End-to-End Smoke Test

**Files:**
- Create: `samples/BadApp/BadApp.csproj`
- Create: `samples/BadApp/Program.cs`
- Create: `samples/BadApp/appsettings.json`

- [ ] **Step 1: Create a sample app with known issues**

```bash
mkdir samples\BadApp
```

Create `samples/BadApp/BadApp.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net48</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="System.Management" Version="8.0.0" />
    <PackageReference Include="Topshelf" Version="4.3.0" />
  </ItemGroup>
</Project>
```

Create `samples/BadApp/Program.cs`:
```csharp
using System.Runtime.InteropServices;
using Microsoft.Win32;

class Program
{
    [DllImport("kernel32.dll")]
    static extern IntPtr GetConsoleWindow();

    static void Main()
    {
        var key = Registry.LocalMachine.OpenSubKey("SOFTWARE");
        var path = @"C:\logs\app.log";
        Console.WriteLine(path);
    }
}
```

Create `samples/BadApp/appsettings.json`:
```json
{
  "ConnectionStrings": {
    "Default": "Server=192.168.1.100;Database=mydb;User Id=sa;Password=secret123"
  },
  "Logging": {
    "LogPath": "C:\\logs\\app.log"
  }
}
```

- [ ] **Step 2: Run the scanner against the sample app**

```bash
dotnet run --project MigrationScanner -- --path samples\BadApp --output samples\BadApp\report.html
```

Expected output:
```
Scanning: samples\BadApp
Found N issue(s): X errors, Y warnings, Z info
Report written to: ...\samples\BadApp\report.html
```

- [ ] **Step 3: Open the report and verify findings appear**

Open `samples\BadApp\report.html` in a browser. Verify:
- Summary shows counts for errors/warnings/info
- P/Invoke finding for `kernel32.dll` is listed
- Windows Namespace finding for `Microsoft.Win32` is listed
- Hardcoded path `C:\logs\app.log` is flagged
- `System.Management` package is flagged as Error
- `Topshelf` package is flagged as Warning
- Filter buttons work (click Errors, Warnings, Info)

- [ ] **Step 4: Commit**

```bash
git add samples/
git commit -m "chore: add sample app with known migration issues for smoke testing"
```

---

## Summary

| Task | Output |
|------|--------|
| 1 | Scaffolded solution with two projects |
| 2 | `Finding`, `Severity`, `ScanContext` models |
| 3 | `IAnalyzer` interface + `ScanOrchestrator` |
| 4-5 | `RoslynAnalyzer` with all rules |
| 6 | `ConfigAnalyzer` for JSON config files |
| 7 | `ProjectAnalyzer` for .csproj files |
| 8 | `NugetAnalyzer` + `KnownWindowsPackages` |
| 9 | `HtmlReporter` self-contained HTML |
| 10 | `Program.cs` CLI wiring |
| 11 | Smoke test against sample bad app |
