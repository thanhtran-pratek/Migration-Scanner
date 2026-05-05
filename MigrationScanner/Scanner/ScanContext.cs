namespace MigrationScanner.Scanner;

public record ScanContext(
    string RootPath,
    IReadOnlyList<string> CsFiles,
    IReadOnlyList<string> ConfigFiles,
    IReadOnlyList<string> ProjectFiles
);
