using MigrationScanner.Analyzers;
using MigrationScanner.Scanner;

namespace MigrationScanner.Tests.Analyzers;

public class ConfigAnalyzerTests
{
    private static async Task<List<Finding>> Analyze(string filename, string content)
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, filename);
        await File.WriteAllTextAsync(file, content);
        var context = new ScanContext(dir, Array.Empty<string>(), new[] { file }, Array.Empty<string>());
        try
        {
            return (await new ConfigAnalyzer().AnalyzeAsync(context)).ToList();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Detects_plaintext_password_in_connection_string()
    {
        var json = """
            {
              "ConnectionStrings": {
                "Default": "Server=myserver;Database=mydb;User Id=sa;Password=secret123"
              }
            }
            """;
        var findings = await Analyze("appsettings.json", json);
        Assert.Contains(findings, f => f.Category == "Hardcoded Secret" && f.Severity == Severity.Error);
    }

    [Fact]
    public async Task Detects_windows_path_in_config_value()
    {
        var json = """
            {
              "Logging": {
                "LogPath": "C:\\logs\\app.log"
              }
            }
            """;
        var findings = await Analyze("appsettings.json", json);
        Assert.Contains(findings, f => f.Category == "Hardcoded Path" && f.Severity == Severity.Warning);
    }

    [Fact]
    public async Task Detects_hardcoded_ip_address()
    {
        var json = """
            {
              "Redis": {
                "Host": "192.168.1.100"
              }
            }
            """;
        var findings = await Analyze("appsettings.json", json);
        Assert.Contains(findings, f => f.Category == "Hardcoded IP" && f.Severity == Severity.Warning);
    }

    [Fact]
    public async Task Does_not_flag_localhost()
    {
        var json = """
            { "Host": "localhost" }
            """;
        var findings = await Analyze("appsettings.json", json);
        Assert.DoesNotContain(findings, f => f.Category == "Hardcoded IP");
    }

    [Fact]
    public async Task Detects_relative_windows_path_in_config()
    {
        var json = """
            {
              "Logging": {
                "LogPath": "logs\\app.log"
              }
            }
            """;
        var findings = await Analyze("appsettings.json", json);
        Assert.Contains(findings, f => f.Category == "Hardcoded Path" && f.Severity == Severity.Warning);
    }

    [Fact]
    public async Task Detects_hardcoded_aws_access_key_id_in_config()
    {
        var json = """
            {
              "AWS": {
                "AccessKey": "AKIAQ4J5XY7Z5HV522UG"
              }
            }
            """;
        var findings = await Analyze("appsettings.json", json);
        Assert.Contains(findings, f => f.Category == "Hardcoded Credential" && f.Severity == Severity.Error);
    }

    [Fact]
    public async Task Detects_hardcoded_secret_in_credential_key()
    {
        var json = """
            {
              "AWS": {
                "SecretKey": "HZGhKkSc7nZoD0c0mNvp"
              }
            }
            """;
        var findings = await Analyze("appsettings.json", json);
        Assert.Contains(findings, f => f.Category == "Hardcoded Credential" && f.Severity == Severity.Error);
    }

    [Fact]
    public async Task Does_not_flag_template_placeholder_credential()
    {
        var json = """
            {
              "AWS": {
                "SecretKey": "${AWS_SECRET_KEY}"
              }
            }
            """;
        var findings = await Analyze("appsettings.json", json);
        Assert.DoesNotContain(findings, f => f.Category == "Hardcoded Credential");
    }
}
