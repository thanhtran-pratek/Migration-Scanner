using MigrationScanner.Analyzers;
using MigrationScanner.Scanner;

namespace MigrationScanner.Tests.Analyzers;

public class RoslynAnalyzerTests
{
    private static async Task<List<Finding>> Analyze(string code)
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "Test.cs");
        await File.WriteAllTextAsync(file, code);
        var context = new ScanContext(dir, new[] { file }, Array.Empty<string>(), Array.Empty<string>());
        try
        {
            return (await new RoslynAnalyzer().AnalyzeAsync(context)).ToList();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Detects_DllImport_with_verbatim_string()
    {
        var code = """
            using System.Runtime.InteropServices;
            class C {
                [DllImport(@"kernel32.dll")]
                static extern IntPtr GetConsoleWindow();
            }
            """;
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Severity == Severity.Error && f.Category == "P/Invoke");
    }

    [Fact]
    public async Task Detects_DllImport_to_windows_dll()
    {
        var code = """
            using System.Runtime.InteropServices;
            class C {
                [DllImport("kernel32.dll")]
                static extern IntPtr GetConsoleWindow();
            }
            """;
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Severity == Severity.Error && f.Category == "P/Invoke");
    }

    [Fact]
    public async Task Detects_windows_only_namespace_using()
    {
        var code = "using Microsoft.Win32;";
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Severity == Severity.Error && f.Category == "Windows Namespace");
    }

    [Fact]
    public async Task Detects_System_Management_namespace()
    {
        var code = "using System.Management;";
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Category == "Windows Namespace");
    }

    [Fact]
    public async Task Does_not_flag_cross_platform_namespaces()
    {
        var code = "using System.Collections.Generic;";
        var findings = await Analyze(code);
        Assert.Empty(findings);
    }

    [Fact]
    public async Task Detects_UseIISIntegration_call()
    {
        var code = """
            class C {
                void M(IWebHostBuilder b) => b.UseIISIntegration();
            }
            """;
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Category == "Windows Hosting" && f.Severity == Severity.Warning);
    }

    [Fact]
    public async Task Detects_UseWindowsService_call()
    {
        var code = """
            class C {
                void M(IHostBuilder b) => b.UseWindowsService();
            }
            """;
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Category == "Windows Hosting");
    }

    [Fact]
    public async Task Detects_hardcoded_windows_path_in_string_literal()
    {
        var code = """
            class C {
                string path = @"C:\logs\app.log";
            }
            """;
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Category == "Hardcoded Path" && f.Severity == Severity.Warning);
    }

    [Fact]
    public async Task Detects_relative_path_with_backslash()
    {
        var code = """
            class C {
                string path = @"logs\app.log";
            }
            """;
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Category == "Hardcoded Path" && f.Severity == Severity.Warning);
    }

    [Fact]
    public async Task Detects_multi_segment_relative_path_with_backslash()
    {
        var code = """
            class C {
                string path = @"config\settings\app.json";
            }
            """;
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Category == "Hardcoded Path" && f.Severity == Severity.Warning);
    }

    [Fact]
    public async Task Does_not_flag_relative_paths()
    {
        var code = """
            class C {
                string path = "logs/app.log";
            }
            """;
        var findings = await Analyze(code);
        Assert.DoesNotContain(findings, f => f.Category == "Hardcoded Path");
    }

    [Fact]
    public async Task Detects_windows_timezone_id()
    {
        var code = """
            using System;
            class C {
                void M() {
                    var tz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                }
            }
            """;
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Category == "Windows Timezone" && f.Severity == Severity.Warning);
    }

    [Fact]
    public async Task Does_not_flag_iana_timezone_id()
    {
        var code = """
            using System;
            class C {
                void M() {
                    var tz = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
                }
            }
            """;
        var findings = await Analyze(code);
        Assert.DoesNotContain(findings, f => f.Category == "Windows Timezone");
    }

    [Fact]
    public async Task CA1416_detects_registry_static_access()
    {
        var code = """
            using Microsoft.Win32;
            class C {
                void M() { var k = Registry.LocalMachine.OpenSubKey("SOFTWARE"); }
            }
            """;
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Category == "CA1416" && f.Severity == Severity.Error);
    }

    [Fact]
    public async Task CA1416_detects_new_eventlog()
    {
        var code = """
            using System.Diagnostics;
            class C {
                void M() { var log = new EventLog("Application"); }
            }
            """;
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Category == "CA1416" && f.Severity == Severity.Error);
    }

    [Fact]
    public async Task CA1416_detects_new_performance_counter()
    {
        var code = """
            using System.Diagnostics;
            class C {
                void M() { var pc = new PerformanceCounter("Processor", "% Processor Time", "_Total"); }
            }
            """;
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Category == "CA1416" && f.Severity == Severity.Error);
    }

    [Fact]
    public async Task CA1416_detects_service_controller()
    {
        var code = """
            using System.ServiceProcess;
            class C {
                void M() { var sc = new ServiceController("W32Time"); }
            }
            """;
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Category == "CA1416" && f.Severity == Severity.Error);
    }

    [Fact]
    public async Task CA1416_does_not_double_report_chained_registry_access()
    {
        var code = """
            using Microsoft.Win32;
            class C {
                void M() { var k = Registry.LocalMachine.OpenSubKey("SOFTWARE"); }
            }
            """;
        var findings = await Analyze(code);
        Assert.Single(findings.Where(f => f.Category == "CA1416"));
    }

    [Fact]
    public async Task Detects_hardcoded_license_key_in_register_call()
    {
        var code = """
            class C {
                void M() {
                    SyncfusionLicenseProvider.RegisterLicense("Mgo+DSMBMAY9C3t2UVhhQlVFf");
                }
            }
            """;
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Category == "Hardcoded License" && f.Severity == Severity.Error);
    }

    [Fact]
    public async Task Detects_hardcoded_license_key_in_set_license_call()
    {
        var code = """
            class C {
                void M() { SetLicense("ABC123XYZ-license-key"); }
                void SetLicense(string key) {}
            }
            """;
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Category == "Hardcoded License" && f.Severity == Severity.Error);
    }

    [Fact]
    public async Task Detects_hardcoded_license_key_in_variable_assignment()
    {
        var code = """
            class C {
                string licenseKey = "Mgo+DSMBMAY9C3t2UVhhQlVFf";
            }
            """;
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Category == "Hardcoded License" && f.Severity == Severity.Error);
    }

    [Fact]
    public async Task Detects_hardcoded_license_key_in_property_assignment()
    {
        var code = """
            class C {
                void M() { LicenseKey = "Mgo+DSMBMAY9C3t2UVhhQlVFf"; }
                string LicenseKey { get; set; }
            }
            """;
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Category == "Hardcoded License" && f.Severity == Severity.Error);
    }

    [Fact]
    public async Task Does_not_flag_empty_string_in_license_call()
    {
        var code = """
            class C {
                void M() { RegisterLicense(""); }
                void RegisterLicense(string key) {}
            }
            """;
        var findings = await Analyze(code);
        Assert.DoesNotContain(findings, f => f.Category == "Hardcoded License");
    }

    [Fact]
    public async Task Detects_hardcoded_aws_access_key_id()
    {
        var code = """
            class C {
                string key = "AKIAQ4J5XY7Z5HV522UG";
            }
            """;
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Category == "Hardcoded Credential" && f.Severity == Severity.Error);
    }

    [Fact]
    public async Task Detects_hardcoded_credential_in_named_variable()
    {
        var code = """
            class C {
                string awsSecretKey = "HZGhKkSc7nZoD0c0mNvp";
            }
            """;
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Category == "Hardcoded Credential" && f.Severity == Severity.Error);
    }

    [Fact]
    public async Task Does_not_flag_non_credential_string()
    {
        var code = """
            class C {
                string bucketName = "my-s3-bucket-prod";
            }
            """;
        var findings = await Analyze(code);
        Assert.DoesNotContain(findings, f => f.Category == "Hardcoded Credential");
    }

    // --- Hardcoded URL ---

    [Fact]
    public async Task Detects_hardcoded_https_url()
    {
        var code = """
            class C {
                string api = "https://api.production.com/v1";
            }
            """;
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Category == "Hardcoded URL" && f.Severity == Severity.Warning);
    }

    [Fact]
    public async Task Detects_hardcoded_http_url_as_method_argument()
    {
        var code = """
            class C {
                void M() { Connect("http://internal.server.com:8080/api"); }
                void Connect(string url) {}
            }
            """;
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Category == "Hardcoded URL" && f.Severity == Severity.Warning);
    }

    [Fact]
    public async Task Does_not_flag_localhost_url()
    {
        var code = """
            class C {
                string dev = "https://localhost:5001/api";
            }
            """;
        var findings = await Analyze(code);
        Assert.DoesNotContain(findings, f => f.Category == "Hardcoded URL");
    }

    [Fact]
    public async Task Url_with_ip_reported_as_url_not_ip()
    {
        var code = """
            class C {
                string addr = "http://192.168.1.100:8080/api";
            }
            """;
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Category == "Hardcoded URL");
        Assert.DoesNotContain(findings, f => f.Category == "Hardcoded IP");
    }

    // --- Hardcoded IP ---

    [Fact]
    public async Task Detects_hardcoded_private_ip()
    {
        var code = """
            class C {
                string host = "192.168.1.100";
            }
            """;
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Category == "Hardcoded IP" && f.Severity == Severity.Warning);
    }

    [Fact]
    public async Task Detects_hardcoded_ip_as_method_argument()
    {
        var code = """
            class C {
                void M() { Connect("10.0.0.5"); }
                void Connect(string host) {}
            }
            """;
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Category == "Hardcoded IP" && f.Severity == Severity.Warning);
    }

    [Fact]
    public async Task Does_not_flag_loopback_ip()
    {
        var code = """
            class C {
                string host = "127.0.0.1";
            }
            """;
        var findings = await Analyze(code);
        Assert.DoesNotContain(findings, f => f.Category == "Hardcoded IP");
    }

    // --- Hardcoded UUID ---

    [Fact]
    public async Task Detects_hardcoded_uuid_in_variable()
    {
        var code = """
            class C {
                string id = "550e8400-e29b-41d4-a716-446655440000";
            }
            """;
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Category == "Hardcoded UUID" && f.Severity == Severity.Warning);
    }

    [Fact]
    public async Task Detects_hardcoded_uuid_with_braces()
    {
        var code = """
            class C {
                string id = "{550e8400-e29b-41d4-a716-446655440000}";
            }
            """;
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Category == "Hardcoded UUID" && f.Severity == Severity.Warning);
    }

    [Fact]
    public async Task Detects_hardcoded_uuid_as_method_argument()
    {
        var code = """
            class C {
                void M() { GetUser("550e8400-e29b-41d4-a716-446655440000"); }
                void GetUser(string id) {}
            }
            """;
        var findings = await Analyze(code);
        Assert.Contains(findings, f => f.Category == "Hardcoded UUID" && f.Severity == Severity.Warning);
    }

    [Fact]
    public async Task Does_not_flag_non_uuid_string()
    {
        var code = """
            class C {
                string s = "not-a-uuid-value";
            }
            """;
        var findings = await Analyze(code);
        Assert.DoesNotContain(findings, f => f.Category == "Hardcoded UUID");
    }
}
