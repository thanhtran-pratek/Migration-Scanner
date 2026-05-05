// MigrationScanner/Analyzers/ConfigAnalyzer.cs
using System.Text.RegularExpressions;
using MigrationScanner.Scanner;

namespace MigrationScanner.Analyzers;

public class ConfigAnalyzer : IAnalyzer
{
    private static readonly Regex WindowsPathRegex = new(
        @"[A-Za-z]:\\" +
        @"|\\\\[a-zA-Z]" +
        @"|[A-Za-z0-9_.]+\\[A-Za-z0-9_.]{2,}",
        RegexOptions.Compiled);
    private static readonly Regex IpAddressRegex = new(@"^(\d{1,3}\.){3}\d{1,3}$", RegexOptions.Compiled);
    private static readonly Regex PasswordInConnStrRegex = new(@"[Pp]assword\s*=\s*[^;'""\s]{3,}", RegexOptions.Compiled);
    private static readonly Regex PwdInConnStrRegex = new(@"\bpwd\s*=\s*[^;'""\s]{3,}", RegexOptions.Compiled);
    private static readonly Regex JsonStringValueRegex = new(@""":\s*""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex AwsAccessKeyIdRegex = new(@"AKIA[0-9A-Z]{16}", RegexOptions.Compiled);
    private static readonly Regex JsonKeyRegex = new(@"""([^""]+)""\s*:", RegexOptions.Compiled);
    private static readonly HashSet<string> CredentialKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "passwd", "secret", "apikey", "api_key", "accesskey", "access_key",
        "token", "credential", "privatekey", "private_key", "awssecret", "aws_secret", "secretkey"
    };

    public async Task<IEnumerable<Finding>> AnalyzeAsync(ScanContext context)
    {
        var findings = new List<Finding>();
        foreach (var file in context.ConfigFiles)
        {
            if (!file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var content = await File.ReadAllTextAsync(file);
                findings.AddRange(AnalyzeJson(file, content));
            }
            catch (IOException)
            {
                // skip unreadable files
            }
        }
        return findings;
    }

    private IEnumerable<Finding> AnalyzeJson(string filePath, string content)
    {
        var lines = content.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineNum = i + 1;
            var snippet = GetSnippet(lines, i);

            var isTemplatePlaceholder = line.Contains("${") || line.Contains("#{") ||
                                        line.Contains("$(") || line.Contains("__") ||
                                        line.Contains("%");

            if (!isTemplatePlaceholder && (PasswordInConnStrRegex.IsMatch(line) || PwdInConnStrRegex.IsMatch(line)))
                yield return new Finding(filePath, lineNum, Severity.Error, "Hardcoded Secret",
                    "Plaintext password found in connection string",
                    snippet,
                    "Move credentials to environment variables or AWS Secrets Manager");

            var valueMatch = JsonStringValueRegex.Match(line);
            if (valueMatch.Success)
            {
                var value = valueMatch.Groups[1].Value;

                if (WindowsPathRegex.IsMatch(value))
                    yield return new Finding(filePath, lineNum, Severity.Warning, "Hardcoded Path",
                        $"Windows path in config value: {value}",
                        snippet,
                        "Use environment variables or relative paths");

                if (IpAddressRegex.IsMatch(value) && !value.StartsWith("127."))
                    yield return new Finding(filePath, lineNum, Severity.Warning, "Hardcoded IP",
                        $"Hardcoded IP address: {value}",
                        snippet,
                        "Use DNS names or service discovery instead of hardcoded IPs");

                if (!isTemplatePlaceholder && AwsAccessKeyIdRegex.IsMatch(value))
                    yield return new Finding(filePath, lineNum, Severity.Error, "Hardcoded Credential",
                        $"AWS Access Key ID hardcoded in config: {value}",
                        snippet,
                        "Use IAM roles or store credentials in AWS Secrets Manager / environment variables");
                else if (!isTemplatePlaceholder && value.Length >= 8 && IsCredentialKey(line))
                    yield return new Finding(filePath, lineNum, Severity.Error, "Hardcoded Credential",
                        $"Credential value hardcoded in config",
                        snippet,
                        "Move secrets to environment variables or AWS Secrets Manager");
            }
        }
    }

    private bool IsCredentialKey(string line)
    {
        var keyMatch = JsonKeyRegex.Match(line);
        if (!keyMatch.Success) return false;
        var key = keyMatch.Groups[1].Value;
        return CredentialKeywords.Any(k => key.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string GetSnippet(string[] lines, int zeroBasedLine)
    {
        var start = Math.Max(0, zeroBasedLine - 1);
        var end = Math.Min(lines.Length - 1, zeroBasedLine + 1);
        return string.Join('\n', lines[start..(end + 1)]);
    }
}
