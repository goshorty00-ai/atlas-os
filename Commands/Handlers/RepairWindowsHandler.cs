using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace AtlasAI.Commands
{
    public class RepairWindowsHandler : ICommandHandler
    {
        public string CommandName => "repair_windows";

        public string GetDescription() => "Run Windows System File Checker (sfc /scannow)";

        public bool CanExecute(CommandContext context) => true;

        public async Task<CommandResult> ExecuteAsync(CommandContext context)
        {
            return await Task.Run(() =>
            {
                var results = new Dictionary<string, object>();

                try
                {
                    // Check if running as administrator
                    if (!IsAdministrator())
                    {
                        return CommandResult.Error(CommandName, 
                            "Administrator privileges required to run System File Checker");
                    }

                    var output = new StringBuilder();
                    var error = new StringBuilder();

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "sfc",
                        Arguments = "/scannow",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        Verb = "runas"
                    };

                    using var process = new Process { StartInfo = startInfo };

                    process.OutputDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            output.AppendLine(e.Data);
                    };

                    process.ErrorDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            error.AppendLine(e.Data);
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    // SFC can take a long time, set timeout to 30 minutes
                    var completed = process.WaitForExit(30 * 60 * 1000);

                    if (!completed)
                    {
                        process.Kill();
                        return CommandResult.Error(CommandName, "System File Checker timed out after 30 minutes");
                    }

                    var exitCode = process.ExitCode;
                    var outputText = output.ToString();

                    results["exit_code"] = exitCode;
                    results["output"] = outputText;

                    if (error.Length > 0)
                        results["error_output"] = error.ToString();

                    var message = exitCode == 0
                        ? "System File Checker completed successfully"
                        : $"System File Checker completed with exit code {exitCode}";

                    // Parse output for common messages
                    if (outputText.Contains("did not find any integrity violations"))
                        message = "No integrity violations found. System files are healthy.";
                    else if (outputText.Contains("found corrupt files and successfully repaired them"))
                        message = "Corrupt files found and repaired successfully.";
                    else if (outputText.Contains("found corrupt files but was unable to fix"))
                        message = "Corrupt files found but could not be repaired automatically.";

                    return CommandResult.Success(CommandName, message, results);
                }
                catch (Exception ex)
                {
                    return CommandResult.Error(CommandName, $"Failed to run System File Checker: {ex.Message}");
                }
            });
        }

        private bool IsAdministrator()
        {
            try
            {
                var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }
}
