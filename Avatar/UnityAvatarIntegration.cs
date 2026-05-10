using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;
using System.Windows;

namespace AtlasAI.Avatar
{
    public class UnityAvatarIntegration
    {
        private const string PIPE_NAME = "Atlas_Avatar";
        private const string UNITY_EXE_NAME = "AtlasAvatar.exe";
        
        private Process unityProcess;
        private bool isUnityRunning = false;
        
        public event Action<string> OnUnityMessage;
        public event Action OnUnityStarted;
        public event Action OnUnityStopped;
        
        public bool IsUnityRunning => isUnityRunning && unityProcess != null && !unityProcess.HasExited;
        
        public async Task<bool> StartUnityAvatarAsync()
        {
            try
            {
                if (IsUnityRunning)
                {
                    return true;
                }
                
                // Find Unity executable
                string unityPath = FindUnityExecutable();
                if (string.IsNullOrEmpty(unityPath))
                {
                    throw new FileNotFoundException("Unity avatar executable not found");
                }
                
                // Start Unity process
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = unityPath,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Normal
                };
                
                unityProcess = Process.Start(startInfo);
                
                if (unityProcess != null)
                {
                    isUnityRunning = true;
                    
                    // Monitor process
                    unityProcess.EnableRaisingEvents = true;
                    unityProcess.Exited += (s, e) =>
                    {
                        isUnityRunning = false;
                        OnUnityStopped?.Invoke();
                    };
                    
                    // Wait a moment for Unity to initialize
                    await Task.Delay(3000);
                    
                    OnUnityStarted?.Invoke();
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to start Unity Avatar: {ex.Message}");
                return false;
            }
        }
        
        public void StopUnityAvatar()
        {
            try
            {
                if (unityProcess != null && !unityProcess.HasExited)
                {
                    unityProcess.CloseMainWindow();
                    
                    // Give it time to close gracefully
                    if (!unityProcess.WaitForExit(5000))
                    {
                        unityProcess.Kill();
                    }
                }
                
                isUnityRunning = false;
                unityProcess = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error stopping Unity: {ex.Message}");
            }
        }
        
        private string FindUnityExecutable()
        {
            // Check common locations
            string[] possiblePaths = {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, UNITY_EXE_NAME),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Unity", UNITY_EXE_NAME),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Avatar", UNITY_EXE_NAME),
                Path.Combine(Directory.GetCurrentDirectory(), UNITY_EXE_NAME),
                Path.Combine(Directory.GetCurrentDirectory(), "Unity", UNITY_EXE_NAME)
            };
            
            foreach (string path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }
            
            return null;
        }
        
        public async Task<bool> SendCommandToUnityAsync(string command)
        {
            if (!IsUnityRunning)
            {
                return false;
            }
            
            try
            {
                using (NamedPipeClientStream pipeClient = new NamedPipeClientStream(".", PIPE_NAME, PipeDirection.Out))
                {
                    await pipeClient.ConnectAsync(1000); // 1 second timeout
                    
                    using (StreamWriter writer = new StreamWriter(pipeClient))
                    {
                        await writer.WriteLineAsync(command);
                        await writer.FlushAsync();
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to send command to Unity: {ex.Message}");
                return false;
            }
        }
        
        // Convenience methods for common avatar actions
        public async Task AvatarSpeakAsync(string message)
        {
            await SendCommandToUnityAsync($"SPEAK|{message}");
        }
        
        public async Task AvatarThinkAsync()
        {
            await SendCommandToUnityAsync("THINK");
        }
        
        public async Task AvatarDanceAsync()
        {
            await SendCommandToUnityAsync("DANCE");
        }
        
        public async Task AvatarJumpAsync()
        {
            await SendCommandToUnityAsync("JUMP");
        }
        
        public async Task AvatarSetStateAsync(string state)
        {
            await SendCommandToUnityAsync($"SET_STATE|{state}");
        }
        
        public async Task AvatarMoveToAsync(float x, float y, float z)
        {
            await SendCommandToUnityAsync($"MOVE_TO|{x},{y},{z}");
        }
        
        public async Task AvatarHealthCheckAsync()
        {
            await SendCommandToUnityAsync("HEALTH_CHECK");
        }
        
        public async Task AvatarVoiceCommandAsync()
        {
            await SendCommandToUnityAsync("VOICE_COMMAND");
        }
        
        public async Task AvatarOrganizeFilesAsync()
        {
            await SendCommandToUnityAsync("ORGANIZE_FILES");
        }
        
        public void Dispose()
        {
            StopUnityAvatar();
        }
    }
    
    // Extension methods for easy integration with existing code
    public static class AvatarIntegrationExtensions
    {
        private static UnityAvatarIntegration _avatarIntegration;
        
        public static UnityAvatarIntegration GetAvatarIntegration()
        {
            if (_avatarIntegration == null)
            {
                _avatarIntegration = new UnityAvatarIntegration();
            }
            return _avatarIntegration;
        }
        
        public static async Task StartAvatarSystemAsync()
        {
            var integration = GetAvatarIntegration();
            await integration.StartUnityAvatarAsync();
        }
        
        public static void StopAvatarSystem()
        {
            var integration = GetAvatarIntegration();
            integration.StopUnityAvatar();
        }
        
        public static async Task ShowAvatarThinkingAsync()
        {
            var integration = GetAvatarIntegration();
            await integration.AvatarThinkAsync();
        }
        
        public static async Task AvatarSayAsync(string message)
        {
            var integration = GetAvatarIntegration();
            await integration.AvatarSpeakAsync(message);
        }
    }
}