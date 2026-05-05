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
