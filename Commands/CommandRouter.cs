using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace AtlasAI.Commands
{
    /// <summary>
    /// Central command router that dispatches commands to appropriate handlers.
    /// </summary>
    public sealed class CommandRouter
    {
        private static CommandRouter? _instance;
        private static readonly object _lock = new();

        public static CommandRouter Instance
        {
            get
            {
                lock (_lock) { return _instance ??= new CommandRouter(); }
            }
        }

        private readonly ConcurrentDictionary<string, ICommandHandler> _handlers = new();
        private readonly ConcurrentDictionary<string, CommandExecutionStats> _stats = new();

        public bool EnableLogging { get; set; } = true;

        private CommandRouter()
        {
            RegisterDefaultHandlers();
        }

        /// <summary>
        /// Register a command handler.
        /// </summary>
        public void RegisterHandler(ICommandHandler handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var commandName = handler.CommandName.ToLowerInvariant();
            _handlers[commandName] = handler;
            Log($"Registered handler: {commandName}");
        }

        /// <summary>
        /// Execute a command by name with arguments.
        /// </summary>
        public async Task<CommandResult> ExecuteAsync(string command, params string[] arguments)
        {
            var context = new CommandContext
            {
                Command = command,
                Arguments = arguments,
                RawInput = $"{command} {string.Join(" ", arguments)}".Trim()
            };

            return await ExecuteAsync(context);
        }

        /// <summary>
        /// Execute a command with full context.
        /// </summary>
        public async Task<CommandResult> ExecuteAsync(CommandContext context)
        {
            var sw = Stopwatch.StartNew();
            var commandName = context.Command.ToLowerInvariant();

            try
            {
                Log($"Executing command: {commandName}");

                if (!_handlers.TryGetValue(commandName, out var handler))
                {
                    return CommandResult.Error(commandName, $"Unknown command: {commandName}");
                }

                if (!handler.CanExecute(context))
                {
                    return CommandResult.Error(commandName, "Command cannot be executed with given parameters");
                }

                var result = await handler.ExecuteAsync(context);
                result.DurationMs = sw.ElapsedMilliseconds;

                RecordStats(commandName, true, sw.ElapsedMilliseconds);
                Log($"Command completed: {commandName} ({sw.ElapsedMilliseconds}ms)");

                return result;
            }
            catch (Exception ex)
            {
                RecordStats(commandName, false, sw.ElapsedMilliseconds);
                Log($"Command failed: {commandName} - {ex.Message}");
                return CommandResult.Error(commandName, ex.Message);
            }
        }

        /// <summary>
        /// Parse natural language input and execute command.
        /// </summary>
        public async Task<CommandResult> ParseAndExecuteAsync(string input)
        {
            var context = ParseInput(input);
            return await ExecuteAsync(context);
        }

        /// <summary>
        /// Get all registered command names.
        /// </summary>
        public IEnumerable<string> GetRegisteredCommands()
        {
            return _handlers.Keys;
        }

        /// <summary>
        /// Get handler for a command.
        /// </summary>
        public ICommandHandler? GetHandler(string command)
        {
            return _handlers.TryGetValue(command.ToLowerInvariant(), out var handler) ? handler : null;
        }

        /// <summary>
        /// Get execution statistics for a command.
        /// </summary>
        public CommandExecutionStats? GetStats(string command)
        {
            return _stats.TryGetValue(command.ToLowerInvariant(), out var stats) ? stats : null;
        }

        /// <summary>
        /// Get all command statistics.
        /// </summary>
        public Dictionary<string, CommandExecutionStats> GetAllStats()
        {
            return _stats.ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        private void RegisterDefaultHandlers()
        {
            RegisterHandler(new ScanSystemHandler());
            RegisterHandler(new KillProcessHandler());
            RegisterHandler(new OptimizeMemoryHandler());
            RegisterHandler(new ScanDownloadsHandler());
            RegisterHandler(new RepairWindowsHandler());
            RegisterHandler(new ShowNetworkActivityHandler());
            RegisterHandler(new CleanTempFilesHandler());
        }

        private CommandContext ParseInput(string input)
        {
            var parts = input.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var command = parts.Length > 0 ? parts[0] : "";
            var arguments = parts.Length > 1 ? parts[1..] : Array.Empty<string>();

            return new CommandContext
            {
                Command = command,
                Arguments = arguments,
                RawInput = input
            };
        }

        private void RecordStats(string command, bool success, long durationMs)
        {
            var stats = _stats.GetOrAdd(command, _ => new CommandExecutionStats { Command = command });

            lock (stats)
            {
                stats.TotalExecutions++;
                if (success) stats.SuccessfulExecutions++;
                else stats.FailedExecutions++;

                stats.TotalDurationMs += durationMs;
                stats.AverageDurationMs = stats.TotalDurationMs / stats.TotalExecutions;
                stats.LastExecutionTime = DateTime.UtcNow;
            }
        }

        private void Log(string message)
        {
            if (EnableLogging)
                Debug.WriteLine($"[CommandRouter] {message}");
        }
    }

    public class CommandExecutionStats
    {
        public string Command { get; set; } = "";
        public long TotalExecutions { get; set; }
        public long SuccessfulExecutions { get; set; }
        public long FailedExecutions { get; set; }
        public long TotalDurationMs { get; set; }
        public long AverageDurationMs { get; set; }
        public DateTime? LastExecutionTime { get; set; }
    }
}
