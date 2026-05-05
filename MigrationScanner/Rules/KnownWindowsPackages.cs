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
