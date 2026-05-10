using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Voice Macros - Record and replay sequences of actions.
    /// "Start recording" -> do stuff -> "Stop recording" -> "Save as morning routine"
    /// </summary>
    public class VoiceMacros
    {
        private static VoiceMacros? _instance;
        public static VoiceMacros Instance => _instance ??= new VoiceMacros();
        
        private readonly string _macrosFile;
        private Dictionary<string, Macro> _macros = new();
        private List<MacroAction> _recordingBuffer = new();
        private bool _isRecording;
        private DateTime _recordingStarted;
        
        public bool IsRecording => _isRecording;
        public event Action<string>? OnRecordingStatusChanged;
        
        private VoiceMacros()
        {
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AtlasAI");
            Directory.CreateDirectory(appData);
            _macrosFile = Path.Combine(appData, "voice_macros.json");
            LoadMacros();
        }
        
        /// <summary>
        /// Start recording a macro
        /// </summary>
        public string StartRecording()
        {
            if (_isRecording)
                return "Already recording! Say 'stop recording' to finish.";
            
            _isRecording = true;
            _recordingBuffer.Clear();
            _recordingStarted = DateTime.Now;
            OnRecordingStatusChanged?.Invoke("recording");
            
            return "🔴 Recording started! I'll remember everything you do. Say 'stop recording' when done.";
        }
        
        /// <summary>
        /// Stop recording and return summary
        /// </summary>
        public string StopRecording()
        {
            if (!_isRecording)
                return "Not currently recording.";
            
            _isRecording = false;
            OnRecordingStatusChanged?.Invoke("stopped");
            
            if (_recordingBuffer.Count == 0)
                return "Recording stopped. No actions were recorded.";
            
            var duration = DateTime.Now - _recordingStarted;
            return $"🔴 Recording stopped!\n" +
                   $"Recorded {_recordingBuffer.Count} actions in {duration.TotalSeconds:F0}s.\n" +
                   $"Say 'save macro as [name]' to save it.";
        }
        
        /// <summary>
        /// Record an action during recording
        /// </summary>
        public void RecordAction(string action, string result)
        {
            if (!_isRecording) return;
            
            _recordingBuffer.Add(new MacroAction
            {
                Command = action,
                Result = result,
                Timestamp = DateTime.Now,
                DelayFromPrevious = _recordingBuffer.Any() 
                    ? (int)(DateTime.Now - _recordingBuffer.Last().Timestamp).TotalMilliseconds 
                    : 0
            });
        }
        
        /// <summary>
        /// Save the recorded macro
        /// </summary>
        public string SaveMacro(string name)
        {
            if (_recordingBuffer.Count == 0)
                return "Nothing to save. Record some actions first!";
            
            var macro = new Macro
            {
                Name = name.ToLowerInvariant(),
                DisplayName = name,
                Actions = new List<MacroAction>(_recordingBuffer),
                CreatedAt = DateTime.Now
            };
            
            _macros[macro.Name] = macro;
            SaveMacros();
            _recordingBuffer.Clear();
            
            return $"✓ Saved macro '{name}' with {macro.Actions.Count} actions.\n" +
                   $"Say '{name}' or 'run {name}' to execute it!";
        }
        
        /// <summary>
        /// Run a saved macro
        /// </summary>
        public async Task<string> RunMacroAsync(string name)
        {
            var macroName = name.ToLowerInvariant();
            
            if (!_macros.TryGetValue(macroName, out var macro))
            {
                // Fuzzy match
                macro = _macros.Values.FirstOrDefault(m => 
                    m.Name.Contains(macroName) || macroName.Contains(m.Name));
            }
            
            if (macro == null)
                return $"❌ Macro '{name}' not found. Say 'list macros' to see available macros.";
            
            var results = new List<string>();
            results.Add($"▶️ Running macro '{macro.DisplayName}'...");
            
            foreach (var action in macro.Actions)
            {
                try
                {
                    // Replay with original timing (capped at 2 seconds)
                    if (action.DelayFromPrevious > 0)
                        await Task.Delay(Math.Min(action.DelayFromPrevious, 2000));
                    
                    var result = await Tools.DirectActionHandler.TryHandleAsync(action.Command);
                    results.Add(result ?? $"✓ {action.Command}");
                }
                catch (Exception ex)
                {
                    results.Add($"❌ {action.Command}: {ex.Message}");
                }
            }
            
            macro.RunCount++;
            macro.LastRun = DateTime.Now;
            SaveMacros();
            
            return string.Join("\n", results);
        }
        
        /// <summary>
        /// Check if input matches a macro name
        /// </summary>
        public async Task<string?> TryRunMacroAsync(string input)
        {
            var lower = input.ToLowerInvariant();
            
            // Direct match
            if (_macros.ContainsKey(lower))
                return await RunMacroAsync(lower);
            
            // "run X" pattern
            if (lower.StartsWith("run "))
            {
                var name = lower.Substring(4).Trim();
                if (_macros.ContainsKey(name) || _macros.Values.Any(m => m.Name.Contains(name)))
                    return await RunMacroAsync(name);
            }
            
            // Fuzzy match for single-word macros
            var match = _macros.Values.FirstOrDefault(m => m.Name == lower);
            if (match != null)
                return await RunMacroAsync(match.Name);
            
            return null;
        }
        
        /// <summary>
        /// List all macros
        /// </summary>
        public string ListMacros()
        {
            if (!_macros.Any())
                return "No macros saved yet. Say 'start recording' to create one!";
            
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("🎬 **Your Voice Macros:**\n");
            
            foreach (var macro in _macros.Values.OrderByDescending(m => m.RunCount))
            {
                sb.AppendLine($"**\"{macro.DisplayName}\"** ({macro.Actions.Count} actions)");
                sb.AppendLine($"   Commands: {string.Join(" → ", macro.Actions.Take(3).Select(a => a.Command))}");
                if (macro.Actions.Count > 3)
                    sb.AppendLine($"   ... and {macro.Actions.Count - 3} more");
                if (macro.RunCount > 0)
                    sb.AppendLine($"   Used {macro.RunCount}x");
                sb.AppendLine();
            }
            
            return sb.ToString();
        }
        
        /// <summary>
        /// Delete a macro
        /// </summary>
        public string DeleteMacro(string name)
        {
            var macroName = name.ToLowerInvariant();
            
            if (_macros.Remove(macroName))
            {
                SaveMacros();
                return $"✓ Deleted macro '{name}'";
            }
            
            return $"❌ Macro '{name}' not found";
        }
        
        /// <summary>
        /// Get recording buffer preview
        /// </summary>
        public string GetRecordingPreview()
        {
            if (!_isRecording)
                return "Not recording.";
            
            if (_recordingBuffer.Count == 0)
                return "🔴 Recording... (no actions yet)";
            
            var actions = string.Join("\n", _recordingBuffer.TakeLast(5).Select(a => $"  • {a.Command}"));
            return $"🔴 Recording ({_recordingBuffer.Count} actions):\n{actions}";
        }
        
        private void LoadMacros()
        {
            try
            {
                if (File.Exists(_macrosFile))
                {
                    var json = File.ReadAllText(_macrosFile);
                    _macros = JsonSerializer.Deserialize<Dictionary<string, Macro>>(json) ?? new();
                }
            }
            catch { _macros = new(); }
        }
        
        private void SaveMacros()
        {
            try
            {
                var json = JsonSerializer.Serialize(_macros, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_macrosFile, json);
            }
            catch { }
        }
    }
    
    public class Macro
    {
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public List<MacroAction> Actions { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public DateTime? LastRun { get; set; }
        public int RunCount { get; set; }
    }
    
    public class MacroAction
    {
        public string Command { get; set; } = "";
        public string Result { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public int DelayFromPrevious { get; set; }
    }
}
