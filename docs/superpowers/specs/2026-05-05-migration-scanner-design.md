# Migration Scanner — Design Spec
**Date:** 2026-05-05
**Author:** thanh.tran@pratek.vn

## Overview

A .NET 9.0 CLI tool that scans a .NET 9.0 solution for issues that would prevent or complicate migration from Windows deployment to AWS via Docker/Linux. Outputs a self-contained HTML report grouped by severity and category.

## Goals

- Detect Windows-specific code, config, and package issues before migration
- Produce a shareable HTML report with severity-based filtering
- Run as a CLI tool pointed at a solution folder or `.sln` file
- Always exit 0 (informational — does not block CI/CD)

## Non-Goals

- Automatic code fixes (report only)
- Runtime environment validation (Docker, AWS config)
- Cross-repo scanning (single solution only)

## CLI Usage

```bash
dotnet run --project MigrationScanner -- --path C:\MyApp\MySolution.sln --output report.html
```

Arguments:
- `--path` (required): Path to `.sln` file or root folder of solution
- `--output` (optional): Output path for HTML report (default: `migration-report.html` in current directory)

Exit code: always 0.

---

## Architecture

### Project Structure

```
MigrationScanner/
├── Program.cs                  # CLI entry point, argument parsing
├── Scanner/
│   ├── ScanOrchestrator.cs     # Discovers files, runs all analyzers, collects findings
│   ├── Finding.cs              # Finding model: file, line, severity, category, message, snippet
│   └── Severity.cs             # Enum: Info / Warning / Error
├── Analyzers/
│   ├── IAnalyzer.cs            # Interface: Task<IEnumerable<Finding>> Analyze(ScanContext)
│   ├── RoslynAnalyzer.cs       # Roslyn AST: Windows APIs, P/Invoke, paths in C# code
│   ├── ConfigAnalyzer.cs       # Regex/JSON: appsettings.json, secrets in config
│   ├── ProjectAnalyzer.cs      # XML: .csproj Windows targets, WinForms/WPF, old TFMs
│   └── NugetAnalyzer.cs        # PackageReference vs curated Windows-only package list
├── Rules/
│   └── KnownWindowsPackages.cs # Curated dictionary: package → severity + suggested alternative
└── Report/
    └── HtmlReporter.cs         # Renders findings into a self-contained HTML file
```

### Data Flow

```
ScanOrchestrator
  ├── Discover .cs files     → RoslynAnalyzer    ┐
  ├── Discover config files  → ConfigAnalyzer    ├── IEnumerable<Finding>
  ├── Discover .csproj files → ProjectAnalyzer   ┤
  └── Discover .csproj files → NugetAnalyzer     ┘
                                    ↓
                             HtmlReporter → report.html
```

---

## Analyzers

### RoslynAnalyzer (`.cs` files)

Uses `Microsoft.CodeAnalysis.CSharp` to parse source into AST. Detects:

| Rule | Severity | Description |
|------|----------|-------------|
| Windows registry access | Error | `Microsoft.Win32.Registry`, `RegistryKey` usage |
| WMI / System.Management | Error | `ManagementObject`, `ManagementScope` usage |
| Windows Service base | Error | `System.ServiceProcess.ServiceBase` |
| P/Invoke to Windows DLLs | Error | `[DllImport("kernel32.dll")]`, `user32`, `advapi32`, etc. |
| IIS/Windows hosting | Warning | `UseIISIntegration()`, `UseWindowsService()` |
| Hardcoded Windows paths | Warning | String literals matching `C:\`, `D:\`, `\\server\` |
| Windows timezone IDs | Warning | `TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time")` etc. |
| Culture/locale pinning | Info | `Thread.CurrentThread.CurrentCulture` set to specific locale |

### ConfigAnalyzer (`.json`, `.xml`, `.yaml`, `.config`)

Uses regex and JSON/XML parsing. Detects:

| Rule | Severity | Description |
|------|----------|-------------|
| Plaintext connection string with password | Error | `Password=`, `pwd=` in connection strings |
| Hardcoded Windows path in config | Warning | Values matching `C:\`, `D:\`, `\\` |
| Hardcoded IP address | Warning | Values matching `\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}` |
| Hardcoded hostname | Info | Non-localhost hostnames in connection strings |
| SMTP credentials in config | Warning | `SmtpPassword`, `EmailPassword` keys with non-empty values |

### ProjectAnalyzer (`.csproj` files)

Uses `System.Xml.Linq` to parse project XML. Detects:

| Rule | Severity | Description |
|------|----------|-------------|
| Windows Runtime Identifier | Error | `<RuntimeIdentifier>win-*</RuntimeIdentifier>` |
| WinForms/WPF enabled | Error | `<UseWindowsForms>true</UseWindowsForms>`, `<UseWPF>true</UseWPF>` |
| Old target framework | Warning | `net48`, `netcoreapp2.x`, `netcoreapp3.x` |
| x86 platform target | Warning | `<PlatformTarget>x86</PlatformTarget>` |
| IIS Express launch profile | Info | `launchSettings.json` with `IIS Express` profile |

### NugetAnalyzer (`.csproj` `PackageReference` elements)

Reads all `PackageReference` entries and checks against a curated list:

| Package | Severity | Alternative |
|---------|----------|-------------|
| `Topshelf` | Warning | Use `IHostedService` / `BackgroundService` |
| `System.Management` | Error | No Linux equivalent — remove or replace |
| `Microsoft.Win32.Registry` | Error | Use environment variables or `IConfiguration` |
| `Nancy` | Warning | Migrate to ASP.NET Core minimal APIs |
| `log4net` (win appender) | Info | Use `Serilog` or `Microsoft.Extensions.Logging` |
| `System.DirectoryServices` | Error | Not supported on Linux without workarounds |
| `Microsoft.Diagnostics.Runtime` | Warning | Limited Linux support |

---

## HTML Report

Single self-contained `.html` file — all CSS and JS inlined, no external dependencies.

### Layout

```
┌─────────────────────────────────────────────────┐
│  Migration Readiness Report                     │
│  Scanned: MySolution.sln  |  Date: 2026-05-05  │
├─────────────────────────────────────────────────┤
│  SUMMARY                                        │
│  ● 12 Errors  ● 8 Warnings  ● 5 Info           │
│  [Colored progress/severity bar]                │
├─────────────────────────────────────────────────┤
│  FINDINGS  [Filter: All | Errors | Warnings]    │
│                                                 │
│  [ERROR]  MyApi/Services/FileService.cs:42      │
│  Category: P/Invoke                             │
│  [DllImport("kernel32.dll")] — Windows-only     │
│  > code snippet (3 lines of context)            │
│  Suggestion: Remove P/Invoke; use cross-...     │
│                                                 │
│  [WARNING]  appsettings.json:15                 │
│  Category: Hardcoded Path                       │
│  "LogPath": "C:\\Logs\\app.log"                 │
│  ...                                            │
├─────────────────────────────────────────────────┤
│  BY CATEGORY  (collapsible sections)            │
│  Windows APIs (4) | Hardcoded Paths (3) | ...   │
└─────────────────────────────────────────────────┘
```

### Features

- Filter buttons: All / Errors / Warnings / Info (pure client-side JS)
- Each finding card: severity badge, file path + line number, category tag, 3-line code snippet, suggested fix hint
- Collapsible sections grouped by category
- Summary counts at top per severity level

---

## Severity Model

| Severity | Meaning |
|----------|---------|
| Error | Will definitively break on Linux — must fix before migration |
| Warning | Likely to break or behave differently — strongly recommended fix |
| Info | Worth reviewing, may be intentional or acceptable |

---

## Dependencies

| Package | Purpose |
|---------|---------|
| `Microsoft.CodeAnalysis.CSharp` | Roslyn C# parsing |
| `System.CommandLine` | CLI argument parsing |
| `System.Text.Json` | JSON config parsing |
| `System.Xml.Linq` | .csproj / XML parsing |

No external runtime dependencies for the HTML report.

---

## Extensibility

New rules can be added by:
1. Implementing `IAnalyzer` and registering in `ScanOrchestrator`
2. Adding entries to `KnownWindowsPackages.cs` for NuGet rules
3. Adding regex patterns to `ConfigAnalyzer` for new config patterns
