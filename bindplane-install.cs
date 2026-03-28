using System;
using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;

class BindPlaneInstall
{
    const string MSI_URL       = "https://bdot.bindplane.com/v1.96.0/observiq-otel-collector.msi";
    const string OPAMP_ENDPOINT = "wss://app.bindplane.com/v1/opamp";
    const string SECRET_KEY    = "xxx";
    const string LABELS        = "xxx";
    const string SERVICE_NAME  = "observiq-otel-collector";

    static int Main()
    {
        if (!IsAdmin())
        {
            Console.WriteLine("ERROR: Must run as Administrator.");
            return 1;
        }

        StopServiceIfRunning();

        string logFile = System.IO.Path.GetTempPath() + "bindplane-install.log";
        string arguments = string.Format(
            "/i \"{0}\" /qn ENABLEMANAGEMENT=\"1\" OPAMPENDPOINT=\"{1}\" OPAMPSECRETKEY=\"{2}\" OPAMPLABELS=\"{3}\" /log \"{4}\"",
            MSI_URL, OPAMP_ENDPOINT, SECRET_KEY, LABELS, logFile);

        Console.WriteLine("[*] Installing BindPlane Agent...");

        var psi = new ProcessStartInfo("msiexec.exe", arguments)
        {
            UseShellExecute  = false,
            CreateNoWindow   = true
        };

        var proc = Process.Start(psi);
        proc.WaitForExit();

        if (proc.ExitCode != 0)
        {
            Console.WriteLine("[-] Installation failed. Exit code: " + proc.ExitCode);
            Console.WriteLine("    Log: " + logFile);
            return 1;
        }

        Console.WriteLine("[+] Installation succeeded.");
        Console.WriteLine("[*] Verifying service...");

        try
        {
            var sc = new ServiceController(SERVICE_NAME);
            Console.WriteLine("[+] Service state: " + sc.Status);
        }
        catch
        {
            Console.WriteLine("[!] Could not query service status.");
        }

        Console.WriteLine("[+] Done. Agent registered to: " + OPAMP_ENDPOINT);
        return 0;
    }

    static void StopServiceIfRunning()
    {
        try
        {
            var sc = new ServiceController(SERVICE_NAME);
            if (sc.Status == ServiceControllerStatus.Running)
            {
                Console.WriteLine("[*] Stopping existing agent service...");
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
            }
        }
        catch { /* service not installed, continue */ }
    }

    static bool IsAdmin()
    {
        var identity  = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}