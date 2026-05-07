// MigrationScanner/Program.cs
using System.CommandLine;
using MigrationScanner.Analyzers;
using MigrationScanner.Report;
using MigrationScanner.Scanner;

var pathOption = new Option<string>("--path")
{
    Description = "Path to the .sln file or root folder of the solution",
    Required = true
};

var outputOption = new Option<string>("--output")
{
    Description = "Output path for the HTML report",
    DefaultValueFactory = _ => "migration-report.html"
};

var rootCommand = new RootCommand("Scans a .NET solution for Windows-to-Linux migration issues");
rootCommand.Add(outputOption);

rootCommand.SetAction(async (ParseResult parseResult) =>
{
    var path = "";
    var output = parseResult.GetValue(outputOption)!;

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
});

return await rootCommand.Parse(args).InvokeAsync();
