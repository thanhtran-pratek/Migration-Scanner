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

    private static readonly HashSet<string> WindowsErrorNamespacePrefixes = new()
    {
        "Microsoft.Win32",
        "System.Management",
        "System.ServiceProcess",
        "System.Windows.Forms",
    };

    private static readonly HashSet<string> WindowsWarningNamespacePrefixes = new()
    {
        "System.Drawing",
    };

    private static readonly HashSet<string> WindowsHostingMethods = new()
    {
        "UseIISIntegration", "UseWindowsService"
    };

    private static readonly HashSet<string> WindowsTimezoneIds = new(StringComparer.Ordinal)
    {
        "Eastern Standard Time", "Central Standard Time", "Mountain Standard Time",
        "Pacific Standard Time", "GMT Standard Time", "Romance Standard Time",
        "W. Europe Standard Time", "Central Europe Standard Time", "SE Asia Standard Time"
    };

    // CA1416: APIs only available on Windows
    private static readonly Dictionary<string, (string Description, string Fix)> WindowsOnlyTypes =
        new(StringComparer.Ordinal)
        {
            ["Registry"]                  = ("Windows Registry API is not available on Linux/macOS (CA1416)",
                                             "Replace with IConfiguration, environment variables, or a cross-platform store"),
            ["RegistryKey"]               = ("Windows Registry API is not available on Linux/macOS (CA1416)",
                                             "Replace with IConfiguration, environment variables, or a cross-platform store"),
            ["EventLog"]                  = ("Windows Event Log is not available on Linux/macOS (CA1416)",
                                             "Use ILogger with a cross-platform logging provider (Serilog, NLog)"),
            ["EventLogEntry"]             = ("Windows Event Log is not available on Linux/macOS (CA1416)",
                                             "Use ILogger with a cross-platform logging provider (Serilog, NLog)"),
            ["PerformanceCounter"]        = ("Windows Performance Counter is not available on Linux/macOS (CA1416)",
                                             "Use System.Diagnostics.Metrics or OpenTelemetry"),
            ["PerformanceCounterCategory"]= ("Windows Performance Counter is not available on Linux/macOS (CA1416)",
                                             "Use System.Diagnostics.Metrics or OpenTelemetry"),
            ["ServiceController"]         = ("Windows Service Controller is not available on Linux/macOS (CA1416)",
                                             "Use IHostedService for cross-platform background services"),
            ["MessageQueue"]              = ("Windows MSMQ is not available on Linux/macOS (CA1416)",
                                             "Use a cross-platform broker: RabbitMQ, Azure Service Bus, or AWS SQS"),
            ["DirectoryEntry"]            = ("Windows Directory Services are not available on Linux/macOS (CA1416)",
                                             "Use a cross-platform LDAP library (Novell.Directory.Ldap.NETStandard)"),
            ["DirectorySearcher"]         = ("Windows Directory Services are not available on Linux/macOS (CA1416)",
                                             "Use a cross-platform LDAP library (Novell.Directory.Ldap.NETStandard)"),
            ["WindowsIdentity"]           = ("WindowsIdentity is Windows-only (CA1416)",
                                             "Use ClaimsPrincipal / IHttpContextAccessor for cross-platform identity"),
            ["WindowsPrincipal"]          = ("WindowsPrincipal is Windows-only (CA1416)",
                                             "Use ClaimsPrincipal for cross-platform identity"),
        };

    // Matches absolute paths (C:\, \\server), and relative paths with backslash separator
    // (e.g. logs\app.log, subfolder\config\settings.json).
    // Requires 2+ chars after the backslash to avoid false positives on \n, \t, etc.
    private static readonly System.Text.RegularExpressions.Regex WindowsPathRegex = new(
        @"^[A-Za-z]:\\" +
        @"|^\\\\[a-zA-Z]" +
        @"|[A-Za-z0-9_.]+\\[A-Za-z0-9_.]{2,}",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex AwsAccessKeyIdRegex =
        new(@"AKIA[0-9A-Z]{16}", System.Text.RegularExpressions.RegexOptions.Compiled);

    // Matches http:// and https:// URLs with a real host (4+ chars after //).
    private static readonly System.Text.RegularExpressions.Regex UrlInStringRegex = new(
        @"https?://[^\s""'<>]{4,}",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    // Matches IPv4 addresses anywhere in a string.
    private static readonly System.Text.RegularExpressions.Regex IpInStringRegex = new(
        @"\b(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})\b",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    // Matches a UUID/GUID in standard hyphenated form, with or without braces.
    private static readonly System.Text.RegularExpressions.Regex GuidInStringRegex = new(
        @"\{?[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}?",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly HashSet<string> CredentialVariableKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "passwd", "secret", "apikey", "api_key", "accesskey", "access_key",
        "token", "credential", "privatekey", "private_key", "awssecret", "aws_secret"
    };

    private static readonly HashSet<string> LicenseVariableKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "license", "licensekey", "license_key", "lickey", "lic_key", "licensetoken"
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
                case MemberAccessExpressionSyntax m:
                    foreach (var f in CheckCA1416MemberAccess(filePath, lines, m)) yield return f;
                    break;
                case ObjectCreationExpressionSyntax oc:
                    foreach (var f in CheckCA1416ObjectCreation(filePath, lines, oc)) yield return f;
                    break;
            }
        }
    }

    private IEnumerable<Finding> CheckUsingDirective(string filePath, string[] lines, UsingDirectiveSyntax node)
    {
        var ns = node.Name?.ToString() ?? "";

        if (WindowsErrorNamespacePrefixes.Any(p => ns == p || ns.StartsWith(p + ".")))
        {
            var line = GetLine(node);
            yield return new Finding(filePath, line, Severity.Error, "Windows Namespace",
                $"Windows-specific namespace: {ns}",
                GetSnippet(lines, line - 1),
                "Remove or replace with a cross-platform alternative");
        }
        else if (WindowsWarningNamespacePrefixes.Any(p => ns == p || ns.StartsWith(p + ".")))
        {
            var line = GetLine(node);
            yield return new Finding(filePath, line, Severity.Warning, "Windows Namespace",
                $"Potentially Windows-specific namespace: {ns}",
                GetSnippet(lines, line - 1),
                "Verify System.Drawing.Common is compatible with your Linux target");
        }
    }

    private IEnumerable<Finding> CheckAttribute(string filePath, string[] lines, AttributeSyntax node)
    {
        var name = node.Name.ToString();
        if (name != "DllImport" && name != "System.Runtime.InteropServices.DllImport")
            yield break;

        var firstArg = node.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
        if (firstArg is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
        {
            var dllArg = lit.Token.ValueText;
            if (WindowsDlls.Contains(dllArg))
            {
                var line = GetLine(node);
                yield return new Finding(filePath, line, Severity.Error, "P/Invoke",
                    $"P/Invoke to Windows-only DLL: {dllArg}",
                    GetSnippet(lines, line - 1),
                    "Remove P/Invoke; use cross-platform .NET APIs instead");
            }
        }
    }

    private IEnumerable<Finding> CheckStringLiteral(string filePath, string[] lines, LiteralExpressionSyntax node)
    {
        var value = node.Token.ValueText;

        if (WindowsPathRegex.IsMatch(value))
        {
            var line = GetLine(node);
            yield return new Finding(filePath, line, Severity.Warning, "Hardcoded Path",
                $"Hardcoded Windows path (backslash separator) in string literal: {value}",
                GetSnippet(lines, line - 1),
                "Use Path.Combine, IConfiguration, or environment variables");
            yield break;
        }

        if (AwsAccessKeyIdRegex.IsMatch(value))
        {
            var line = GetLine(node);
            yield return new Finding(filePath, line, Severity.Error, "Hardcoded Credential",
                $"AWS Access Key ID hardcoded in source: {value}",
                GetSnippet(lines, line - 1),
                "Use IAM roles or store credentials in AWS Secrets Manager / environment variables");
            yield break;
        }

        if (value.Length >= 8 && IsCredentialAssignment(node))
        {
            var line = GetLine(node);
            yield return new Finding(filePath, line, Severity.Error, "Hardcoded Credential",
                $"Hardcoded credential value in source",
                GetSnippet(lines, line - 1),
                "Move secrets to environment variables, IConfiguration, or AWS Secrets Manager");
            yield break;
        }

        if (value.Length > 0 && IsLicenseAssignment(node))
        {
            var line = GetLine(node);
            yield return new Finding(filePath, line, Severity.Error, "Hardcoded License",
                $"License key hardcoded in source: \"{value}\"",
                GetSnippet(lines, line - 1),
                "Store license keys in environment variables or a secrets manager");
            yield break;
        }

        // URL is checked before IP so a URL containing an IP isn't reported twice.
        var urlMatch = UrlInStringRegex.Match(value);
        if (urlMatch.Success && !IsLocalhostUrl(urlMatch.Value))
        {
            var line = GetLine(node);
            yield return new Finding(filePath, line, Severity.Warning, "Hardcoded URL",
                $"URL hardcoded in source: {value}",
                GetSnippet(lines, line - 1),
                "Move URLs to IConfiguration or environment variables");
            yield break;
        }

        var ipMatch = IpInStringRegex.Match(value);
        if (ipMatch.Success)
        {
            var ip = ipMatch.Value;
            if (!ip.StartsWith("127.") && ip != "0.0.0.0")
            {
                var line = GetLine(node);
                yield return new Finding(filePath, line, Severity.Warning, "Hardcoded IP",
                    $"IP address hardcoded in source: {ip}",
                    GetSnippet(lines, line - 1),
                    "Use DNS names or service discovery instead of hardcoded IPs");
            }
        }

        if (GuidInStringRegex.IsMatch(value))
        {
            var line = GetLine(node);
            yield return new Finding(filePath, line, Severity.Warning, "Hardcoded UUID",
                $"UUID/GUID hardcoded in source: {value}",
                GetSnippet(lines, line - 1),
                "Move hardcoded IDs to IConfiguration, constants, or database seed data");
        }
    }

    private static bool IsLocalhostUrl(string url)
    {
        var schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0) return false;
        var hostStart = schemeEnd + 3;
        var hostEnd = url.IndexOfAny(['/', ':', '?', '#'], hostStart);
        var host = hostEnd < 0 ? url[hostStart..] : url[hostStart..hostEnd];
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.StartsWith("127.", StringComparison.Ordinal)
            || host == "::1"
            || host == "0.0.0.0";
    }

    private static bool IsLicenseAssignment(LiteralExpressionSyntax node)
    {
        var identifier = node.Parent switch
        {
            EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax v } => v.Identifier.Text,
            AssignmentExpressionSyntax a => (a.Left as IdentifierNameSyntax)?.Identifier.Text
                                         ?? (a.Left as MemberAccessExpressionSyntax)?.Name.Identifier.Text,
            _ => null
        };
        if (identifier == null) return false;
        return LicenseVariableKeywords.Any(k =>
            identifier.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool IsCredentialAssignment(LiteralExpressionSyntax node)
    {
        // Check if this literal is assigned to a variable/field whose name suggests a credential
        var identifier = node.Parent switch
        {
            EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax v } => v.Identifier.Text,
            AssignmentExpressionSyntax a => (a.Left as IdentifierNameSyntax)?.Identifier.Text
                                         ?? (a.Left as MemberAccessExpressionSyntax)?.Name.Identifier.Text,
            _ => null
        };
        if (identifier == null) return false;
        return CredentialVariableKeywords.Any(k =>
            identifier.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
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

        if (methodName != null &&
            methodName.IndexOf("license", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var firstArg = node.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
            if (firstArg is LiteralExpressionSyntax licLit &&
                licLit.IsKind(SyntaxKind.StringLiteralExpression) &&
                licLit.Token.ValueText.Length > 0)
            {
                var line = GetLine(node);
                yield return new Finding(filePath, line, Severity.Error, "Hardcoded License",
                    $"License key hardcoded as argument to {methodName}(): \"{licLit.Token.ValueText}\"",
                    GetSnippet(lines, line - 1),
                    "Store license keys in environment variables or a secrets manager");
            }
        }

        if (methodName == "FindSystemTimeZoneById")
        {
            var arg = node.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
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

    // CA1416: flag `Registry.LocalMachine`, `EventLog.WriteEntry(...)`, etc.
    // The `node.Expression is IdentifierNameSyntax` guard ensures we only match the
    // innermost access (e.g. `Registry.LocalMachine`), not the chained outer access
    // (`Registry.LocalMachine.OpenSubKey`), so no duplicate findings are emitted.
    private IEnumerable<Finding> CheckCA1416MemberAccess(string filePath, string[] lines, MemberAccessExpressionSyntax node)
    {
        if (node.Expression is not IdentifierNameSyntax receiver) yield break;
        var typeName = receiver.Identifier.Text;
        if (!WindowsOnlyTypes.TryGetValue(typeName, out var info)) yield break;

        var line = GetLine(node);
        yield return new Finding(filePath, line, Severity.Error, "CA1416",
            $"{info.Description}: {typeName}.{node.Name.Identifier.Text}",
            GetSnippet(lines, line - 1),
            info.Fix);
    }

    // CA1416: flag `new EventLog(...)`, `new PerformanceCounter(...)`, etc.
    private IEnumerable<Finding> CheckCA1416ObjectCreation(string filePath, string[] lines, ObjectCreationExpressionSyntax node)
    {
        var typeName = node.Type switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            QualifiedNameSyntax q   => q.Right.Identifier.Text,
            _                       => null
        };
        if (typeName == null || !WindowsOnlyTypes.TryGetValue(typeName, out var info)) yield break;

        var line = GetLine(node);
        yield return new Finding(filePath, line, Severity.Error, "CA1416",
            $"{info.Description}: new {typeName}()",
            GetSnippet(lines, line - 1),
            info.Fix);
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
