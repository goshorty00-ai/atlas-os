using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace AtlasAI.Commands
{
    public class KillProcessHandler : ICommandHandler
    {
        public string CommandName => "kill_process";

        public string GetDescription() => "Terminate a running process by name or PID";

        public bool CanExecute(CommandContext context)
        {
            return context.Arguments.Length > 0 || context.Parameters.ContainsKey("process");
        }

        public async Task<CommandResult> ExecuteAsync(CommandContext context)
        {
            return await Task.Run(() =>
            {
                var processName = context.Arguments.Length > 0
                    ? context.Arguments[0]
                    : context.Parameters.TryGetValue("process", out var p) ? p.ToString() : null;

                if (string.IsNullOrWhiteSpace(processName))
                {
                    return CommandResult.Error(CommandName, "Process name or PID required");
                }

                processName = processName.Replace(".exe", "").Trim();

                // Try as PID first
                if (int.TryParse(processName, out var pid))
                {
                    return KillByPid(pid);
                }

                // Kill by name
                return KillByName(processName);
            });
        }

        private CommandResult KillByPid(int pid)
        {
            try
            {
                var process = Process.GetProcessById(pid);
                var name = process.ProcessName;
                process.Kill();
                process.WaitForExit(5000);

                return CommandResult.Success(CommandName, $"Process terminated: {name} (PID: {pid})", new Dictionary<string, object>
                {
                    ["process"] = name,
                    ["pid"] = pid,
                    ["method"] = "pid"
                });
            }
            catch (ArgumentException)
            {
                return CommandResult.Error(CommandName, $"Process with PID {pid} not found");
            }
            catch (Exception ex)
            {
                return CommandResult.Error(CommandName, $"Failed to kill process {pid}: {ex.Message}");
            }
        }

        private CommandResult KillByName(string processName)
        {
            try
            {
                var processes = Process.GetProcessesByName(processName);

                if (processes.Length == 0)
                {
                    return CommandResult.Error(CommandName, $"Process '{processName}' not found");
                }

                var killed = 0;
                var pids = new List<int>();

                foreach (var proc in processes)
                {
                    try
                    {
                        pids.Add(proc.Id);
                        proc.Kill();
                        proc.WaitForExit(5000);
                        killed++;
                    }
                    catch { }
                }

                if (killed == 0)
                {
                    return CommandResult.Error(CommandName, $"Failed to terminate any instances of '{processName}'");
                }

                return CommandResult.Success(
                    CommandName,
                    $"Terminated {killed} instance(s) of {processName}.exe",
                    new Dictionary<string, object>
                    {
                        ["process"] = processName,
                        ["instances_killed"] = killed,
                        ["pids"] = pids,
                        ["method"] = "name"
                    });
            }
            catch (Exception ex)
            {
                return CommandResult.Error(CommandName, $"Error killing process: {ex.Message}");
            }
        }
    }
}
