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
