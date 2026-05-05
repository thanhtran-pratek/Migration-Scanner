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
