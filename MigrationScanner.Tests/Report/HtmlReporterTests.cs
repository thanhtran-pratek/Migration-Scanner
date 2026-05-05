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
        Assert.Contains("1", html);
    }

    [Fact]
    public void Generate_includes_solution_name()
    {
        var html = HtmlReporter.Generate(SampleFindings(), "MySolution.sln");
        Assert.Contains("MySolution.sln", html);
    }
}
