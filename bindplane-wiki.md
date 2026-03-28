# BindPlane Agent — Windows Endpoint Deployment via CrowdStrike RTR

## Overview

This guide covers creating a BindPlane agent installer that can be deployed to Windows endpoints via CrowdStrike Fusion RTR **Put and Run**. Since RTR's `run` command only executes `.exe` files, the install script must be compiled into a binary.

---

## Prerequisites

- BindPlane OP server with an agent secret key
- CrowdStrike Falcon with RTR Active Responder role
- Windows machine with .NET Framework 4.x (for compilation)
- VS Code or any text editor

---

## Step 1 C#

Create a file named `bindplane-install.cs` with the following content. Update the four constants at the top with your environment values.

```csharp
using System;
using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;

class BindPlaneInstall
{
    const string MSI_URL        = "https://YOUR-BINDPLANE-SERVER/vX.XX.X/observiq-otel-collector.msi";
    const string OPAMP_ENDPOINT = "wss://app.bindplane.com/v1/opamp";
    const string SECRET_KEY     = "YOUR-SECRET-KEY";
    const string LABELS         = "configuration=Windows_OS";
    const string SERVICE_NAME   = "observiq-otel-collector";

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
            UseShellExecute = false,
            CreateNoWindow  = true
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
```

---

## Step 2 — Compile to EXE

Open **cmd as Administrator** and run the .NET Framework C# compiler:

```
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /out:bindplane-install.exe /target:winexe /reference:System.ServiceProcess.dll bindplane-install.cs
```

> If `csc.exe` is not at that path, search for it:
> ```batch
> dir /s C:\Windows\Microsoft.NET\csc.exe
> ```
> Use the path from the highest .NET Framework version found.

A successful compile outputs:
```
Microsoft (R) Visual C# Compiler version X.X.X
Copyright (C) Microsoft Corporation. All rights reserved.
```

No errors = `bindplane-install.exe` is ready.

---

## Step 3 — Test Locally

Before uploading to CrowdStrike, verify the exe works:

1. Right-click `bindplane-install.exe` → **Run as administrator**
2. Check install log: `%TEMP%\bindplane-install.log`
3. Verify service: `sc query observiq-otel-collector`
4. Confirm agent appears in BindPlane UI under **Agents**

---

## Step 4 — Upload to CrowdStrike RTR Files

1. In Falcon console: **Response → Files → Add File**
2. Upload `bindplane-install.exe`
3. Set description: `BindPlane Agent Installer vX.XX.X`

---

## Step 5 — Deploy via Fusion Workflow

In your Fusion workflow, add an **RTR - Put and Run** action:

| Field | Value |
|---|---|
| **File** | `bindplane-install.exe` |
| **Command** | `bindplane-install.exe` |
| **Wait for completion** | Yes |

RTR runs as SYSTEM (which is effectively Administrator), so no elevation prompts occur on the endpoint.

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| No install log created | msiexec never ran | Check exe is running as admin; remove `net session` checks |
| `dial tcp [::1]:3001` in agent log | Old `manager.yaml` has stale endpoint | Edit `C:\Program Files\observIQ OpenTelemetry Collector\manager.yaml`, update `endpoint:` value, restart service |
| Agent installs but doesn't appear in BindPlane | Wrong secret key or endpoint | Verify `OPAMP_SECRET_KEY` matches key shown in BindPlane UI → Install Agent wizard |
| `1603` in install log | msiexec permission error | Ensure exe runs as admin/SYSTEM |
| `1638` in install log | Older version already installed | Add uninstall step before msiexec, or use `/fv` flag |

---

## Notes

- The secret key is embedded in the binary. Treat `bindplane-install.exe` as sensitive — do not share publicly.
- To update the key or endpoint, edit the constants in `bindplane-install.cs` and recompile.
- The compiled exe works on any Windows endpoint regardless of where it was compiled.
