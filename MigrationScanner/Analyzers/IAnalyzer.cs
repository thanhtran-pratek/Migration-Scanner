// MigrationScanner/Analyzers/IAnalyzer.cs
using MigrationScanner.Scanner;

namespace MigrationScanner.Analyzers;

public interface IAnalyzer
{
    Task<IEnumerable<Finding>> AnalyzeAsync(ScanContext context);
}
