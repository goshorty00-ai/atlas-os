using System.Threading.Tasks;

namespace AtlasAI.Commands
{
    /// <summary>
    /// Interface for command handlers.
    /// </summary>
    public interface ICommandHandler
    {
        /// <summary>
        /// Command name this handler processes.
        /// </summary>
        string CommandName { get; }

        /// <summary>
        /// Execute the command with given parameters.
        /// </summary>
        Task<CommandResult> ExecuteAsync(CommandContext context);

        /// <summary>
        /// Check if the handler can execute with given parameters.
        /// </summary>
        bool CanExecute(CommandContext context);

        /// <summary>
        /// Get command description for help/documentation.
        /// </summary>
        string GetDescription();
    }

    /// <summary>
    /// Context passed to command handlers.
    /// </summary>
    public class CommandContext
    {
        public string Command { get; set; } = "";
        public string[] Arguments { get; set; } = System.Array.Empty<string>();
        public System.Collections.Generic.Dictionary<string, object> Parameters { get; set; } = new();
        public string RawInput { get; set; } = "";
        public string Source { get; set; } = "unknown";
    }
}
