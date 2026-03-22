using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace GameCaptureManagerLauncher
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string exeDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory;
            string scriptPath = Path.Combine(exeDirectory, "GameCaptureManager.ps1");

            if (!File.Exists(scriptPath))
            {
                MessageBox.Show(
                    "Could not find GameCaptureManager.ps1 next to the launcher.\n\nExpected at:\n" + scriptPath,
                    "PixelVault",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            string powerShellPath = FindPowerShell();
            if (string.IsNullOrWhiteSpace(powerShellPath))
            {
                MessageBox.Show(
                    "Could not find PowerShell on this system. Install PowerShell 7 or use Windows PowerShell.",
                    "PixelVault",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = powerShellPath,
                Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + scriptPath + "\"",
                WorkingDirectory = exeDirectory,
                UseShellExecute = true
            };

            try
            {
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "PixelVault",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static string FindPowerShell()
        {
            string[] candidates = new[]
            {
                "pwsh.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
                "powershell.exe"
            };

            foreach (string candidate in candidates)
            {
                try
                {
                    if (candidate.Contains(Path.DirectorySeparatorChar.ToString()) || candidate.Contains(Path.AltDirectorySeparatorChar.ToString()))
                    {
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                    else
                    {
                        using (Process process = new Process())
                        {
                            process.StartInfo = new ProcessStartInfo
                            {
                                FileName = candidate,
                                Arguments = "-NoProfile -Command \"$PSVersionTable.PSVersion.ToString()\"",
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true
                            };

                            if (process.Start())
                            {
                                process.WaitForExit(3000);
                                if (process.ExitCode == 0)
                                {
                                    return candidate;
                                }
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            return null;
        }
    }
}

