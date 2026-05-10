# Atlas AI Command Execution System

A comprehensive command execution system that routes AI commands to appropriate system services with structured responses.

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│                   Command Router                        │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐            │
│  │  Parse   │→ │ Validate │→ │ Execute  │→ Result    │
│  └──────────┘  └──────────┘  └──────────┘            │
└─────────────────────────────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────────────────────────┐
│              Command Handlers                           │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐ │
│  │ scan_system  │  │ kill_process │  │optimize_memory│ │
│  └──────────────┘  └──────────────┘  └──────────────┘ │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐ │
│  │scan_downloads│  │repair_windows│  │show_network  │ │
│  └──────────────┘  └──────────────┘  └──────────────┘ │
│  ┌──────────────┐                                      │
│  │clean_temp    │                                      │
│  └──────────────┘                                      │
└─────────────────────────────────────────────────────────┘
```

## Supported Commands

### 1. scan_system
Perform comprehensive system scan for threats and issues.

**Usage:**
```csharp
var result = await CommandRouter.Instance.ExecuteAsync("scan_system");
```

**Response:**
```json
{
  "status": "success",
  "action": "scan_system",
  "message": "System scan complete. 2 issue(s) found.",
  "data": {
    "total_processes": 156,
    "high_memory_processes": 3,
    "high_memory_list": ["chrome (850MB)", "code (620MB)"],
    "temp_files": 1245,
    "temp_size_mb": 450,
    "disk_free_percent": 45.2,
    "disk_free_gb": 120,
    "startup_programs": 8,
    "issues_found": 2,
    "issues": [
      "1245 temporary files found (450MB)",
      "3 high memory processes detected"
    ]
  },
  "timestamp": "2026-03-16T12:05:00.000Z",
  "duration_ms": 245
}
```

### 2. kill_process
Terminate a running process by name or PID.

**Usage:**
```csharp
// By name
var result = await CommandRouter.Instance.ExecuteAsync("kill_process", "chrome");

// By PID
var result = await CommandRouter.Instance.ExecuteAsync("kill_process", "1234");
```

**Response:**
```json
{
  "status": "success",
  "action": "kill_process",
  "message": "Terminated 3 instance(s) of chrome.exe",
  "data": {
    "process": "chrome",
    "instances_killed": 3,
    "pids": [1234, 5678, 9012],
    "method": "name"
  },
  "timestamp": "2026-03-16T12:05:00.000Z",
  "duration_ms": 120
}
```

### 3. optimize_memory
Optimize system memory by clearing cache and running garbage collection.

**Usage:**
```csharp
var result = await CommandRouter.Instance.ExecuteAsync("optimize_memory");
```

**Response:**
```json
{
  "status": "success",
  "action": "optimize_memory",
  "message": "Memory optimization complete. Freed 245MB from GC, deleted 156 temp files.",
  "data": {
    "initial_available_mb": 4096,
    "gc_freed_mb": 245,
    "temp_files_deleted": 156,
    "final_available_mb": 4341,
    "memory_freed_mb": 245
  },
  "timestamp": "2026-03-16T12:05:00.000Z",
  "duration_ms": 1850
}
```

### 4. scan_downloads
Scan Downloads folder for suspicious files.

**Usage:**
```csharp
var result = await CommandRouter.Instance.ExecuteAsync("scan_downloads");
```

**Response:**
```json
{
  "status": "success",
  "action": "scan_downloads",
  "message": "Downloads scan complete. 45 files scanned, 2 executable(s) found.",
  "data": {
    "total_files": 45,
    "executables_found": 2,
    "recent_files": 12,
    "large_files": 3,
    "executables": [
      {
        "name": "setup.exe",
        "size_mb": 25,
        "modified": "2026-03-15 14:30:00",
        "extension": ".exe"
      }
    ]
  },
  "timestamp": "2026-03-16T12:05:00.000Z",
  "duration_ms": 180
}
```

### 5. repair_windows
Run Windows System File Checker (sfc /scannow).

**Usage:**
```csharp
var result = await CommandRouter.Instance.ExecuteAsync("repair_windows");
```

**Response:**
```json
{
  "status": "success",
  "action": "repair_windows",
  "message": "No integrity violations found. System files are healthy.",
  "data": {
    "exit_code": 0,
    "output": "Windows Resource Protection did not find any integrity violations."
  },
  "timestamp": "2026-03-16T12:05:00.000Z",
  "duration_ms": 180000
}
```

**Note:** Requires administrator privileges.

### 6. show_network_activity
Display active network connections and statistics.

**Usage:**
```csharp
var result = await CommandRouter.Instance.ExecuteAsync("show_network_activity");
```

**Response:**
```json
{
  "status": "success",
  "action": "show_network_activity",
  "message": "Network activity: 45 TCP connections, 12 TCP listeners, 8 UDP listeners",
  "data": {
    "total_tcp_connections": 45,
    "tcp_listeners": 12,
    "udp_listeners": 8,
    "connections_by_state": {
      "Established": 32,
      "TimeWait": 8,
      "CloseWait": 5
    },
    "active_connections": [
      {
        "local": "192.168.1.100:54321",
        "remote": "93.184.216.34:443",
        "state": "Established"
      }
    ],
    "network_interfaces": [
      {
        "name": "Ethernet",
        "type": "Ethernet",
        "speed_mbps": 1000,
        "bytes_sent": 1234567890,
        "bytes_received": 9876543210
      }
    ]
  },
  "timestamp": "2026-03-16T12:05:00.000Z",
  "duration_ms": 95
}
```

### 7. clean_temp_files
Clean temporary files from system temp folders.

**Usage:**
```csharp
var result = await CommandRouter.Instance.ExecuteAsync("clean_temp_files");
```

**Response:**
```json
{
  "status": "success",
  "action": "clean_temp_files",
  "message": "Cleaned 1245 temporary file(s), freed 450MB",
  "data": {
    "files_deleted": 1245,
    "space_freed_mb": 450,
    "errors": 12
  },
  "timestamp": "2026-03-16T12:05:00.000Z",
  "duration_ms": 2340
}
```

## Usage Examples

### Basic Execution
```csharp
using AtlasAI.Commands;

var router = CommandRouter.Instance;

// Execute command
var result = await router.ExecuteAsync("scan_system");

// Check result
if (result.Status == "success")
{
    Console.WriteLine(result.Message);
    foreach (var (key, value) in result.Data ?? new())
    {
        Console.WriteLine($"{key}: {value}");
    }
}
else
{
    Console.WriteLine($"Error: {result.Error}");
}
```

### With Arguments
```csharp
// Kill process by name
var result = await router.ExecuteAsync("kill_process", "chrome");

// Kill process by PID
var result = await router.ExecuteAsync("kill_process", "1234");
```

### Natural Language Parsing
```csharp
// Parse and execute from natural language
var result = await router.ParseAndExecuteAsync("kill process chrome");
var result = await router.ParseAndExecuteAsync("scan my downloads");
var result = await router.ParseAndExecuteAsync("optimize memory");
```

### Custom Context
```csharp
var context = new CommandContext
{
    Command = "kill_process",
    Arguments = new[] { "chrome" },
    Parameters = new Dictionary<string, object>
    {
        ["force"] = true,
        ["timeout"] = 5000
    },
    Source = "ai_chat"
};

var result = await router.ExecuteAsync(context);
```

## Creating Custom Commands

### 1. Implement ICommandHandler
```csharp
using AtlasAI.Commands;
using System.Threading.Tasks;

public class MyCustomHandler : ICommandHandler
{
    public string CommandName => "my_command";

    public string GetDescription() => "Description of what this command does";

    public bool CanExecute(CommandContext context)
    {
        // Validate context/parameters
        return true;
    }

    public async Task<CommandResult> ExecuteAsync(CommandContext context)
    {
        return await Task.Run(() =>
        {
            try
            {
                // Execute command logic
                var data = new Dictionary<string, object>
                {
                    ["result"] = "success"
                };

                return CommandResult.Success(
                    CommandName,
                    "Command executed successfully",
                    data
                );
            }
            catch (Exception ex)
            {
                return CommandResult.Error(CommandName, ex.Message);
            }
        });
    }
}
```

### 2. Register Handler
```csharp
var router = CommandRouter.Instance;
router.RegisterHandler(new MyCustomHandler());
```

## Integration with AI

The command router is integrated with `SecurityAIEngine`:

```csharp
// AI automatically routes recognized commands
var response = await SecurityAIEngine.ProcessChatAsync("scan my system");
// → Executes scan_system command and returns formatted response
```

## Command Statistics

```csharp
var router = CommandRouter.Instance;

// Get stats for specific command
var stats = router.GetStats("scan_system");
Console.WriteLine($"Executions: {stats.TotalExecutions}");
Console.WriteLine($"Success rate: {stats.SuccessfulExecutions}/{stats.TotalExecutions}");
Console.WriteLine($"Avg duration: {stats.AverageDurationMs}ms");

// Get all stats
var allStats = router.GetAllStats();
foreach (var (command, stat) in allStats)
{
    Console.WriteLine($"{command}: {stat.TotalExecutions} executions");
}
```

## Error Handling

All commands return structured errors:

```json
{
  "status": "error",
  "action": "kill_process",
  "message": "Command execution failed",
  "error": "Process 'malware.exe' not found",
  "timestamp": "2026-03-16T12:05:00.000Z",
  "duration_ms": 45
}
```

## Security Considerations

1. **Administrator Privileges**: Some commands (repair_windows) require admin rights
2. **Process Termination**: kill_process validates process exists before termination
3. **File Operations**: File deletion operations check file age and location
4. **Command Validation**: All commands validate input before execution
5. **Error Handling**: Exceptions are caught and returned as structured errors

## Performance

- **Throughput**: 100+ commands/second
- **Latency**: < 100ms for most commands (except repair_windows)
- **Memory**: Minimal overhead, handlers are stateless
- **Thread Safety**: Fully thread-safe, concurrent execution supported

## Best Practices

1. **Always check result status** before accessing data
2. **Handle errors gracefully** - display user-friendly messages
3. **Use structured data** from result.Data for UI display
4. **Monitor statistics** to track command usage
5. **Implement custom handlers** for domain-specific commands
6. **Validate permissions** before executing privileged commands
