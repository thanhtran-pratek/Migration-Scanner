using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using Microsoft.Win32;

class Program
{
    [DllImport("kernel32.dll")]
    static extern IntPtr GetConsoleWindow();

    static void Main()
    {
        // CA1416: Windows Registry
        var key = Registry.LocalMachine.OpenSubKey("SOFTWARE");

        // CA1416: Windows Event Log
        var log = new EventLog("Application");
        log.WriteEntry("App started");

        // CA1416: Windows Performance Counter
        var cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total");

        // CA1416: Windows Service Controller
        var svc = new ServiceController("W32Time");

        var path = @"C:\logs\app.log";
        Console.WriteLine(path);
    }
}
