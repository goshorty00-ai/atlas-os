using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Edge TTS Provider - Free, high-quality neural voices via Microsoft Edge.
    /// Used as: Development voice, Fallback voice, Offline-capable option.
    /// Requires: Python with edge-tts package (pip install edge-tts)
    /// </summary>
    public class EdgeTtsProvider : IVoiceProvider
    {
        private CancellationTokenSource? _currentCts;
        private Process? _currentProcess;
        private bool _isAvailable;
        private bool _checkedAvailability;

        // Neural voice options (Microsoft Edge TTS - free, high quality)
        private static readonly VoiceInfo[] BuiltInVoices = new[]
        {
            new VoiceInfo { Id = "en-US-JennyNeural", DisplayName = "Jenny (US Female)", Gender = "Female", Locale = "en-US", IsCloud = false, Provider = VoiceProviderType.EdgeTTS },
            new VoiceInfo { Id = "en-US-GuyNeural", DisplayName = "Guy (US Male)", Gender = "Male", Locale = "en-US", IsCloud = false, Provider = VoiceProviderType.EdgeTTS },
            new VoiceInfo { Id = "en-US-AriaNeural", DisplayName = "Aria (US Female)", Gender = "Female", Locale = "en-US", IsCloud = false, Provider = VoiceProviderType.EdgeTTS },
            new VoiceInfo { Id = "en-US-DavisNeural", DisplayName = "Davis (US Male)", Gender = "Male", Locale = "en-US", IsCloud = false, Provider = VoiceProviderType.EdgeTTS },
            new VoiceInfo { Id = "en-GB-SoniaNeural", DisplayName = "Sonia (UK Female)", Gender = "Female", Locale = "en-GB", IsCloud = false, Provider = VoiceProviderType.EdgeTTS },
            new VoiceInfo { Id = "en-GB-RyanNeural", DisplayName = "Ryan (UK Male)", Gender = "Male", Locale = "en-GB", IsCloud = false, Provider = VoiceProviderType.EdgeTTS },
            new VoiceInfo { Id = "en-AU-NatashaNeural", DisplayName = "Natasha (AU Female)", Gender = "Female", Locale = "en-AU", IsCloud = false, Provider = VoiceProviderType.EdgeTTS },
            new VoiceInfo { Id = "en-AU-WilliamNeural", DisplayName = "William (AU Male)", Gender = "Male", Locale = "en-AU", IsCloud = false, Provider = VoiceProviderType.EdgeTTS },
        };

        public VoiceProviderType ProviderType => VoiceProviderType.EdgeTTS;
        public string DisplayName => "Edge TTS (Local)";
        public bool RequiresInternet => true; // Edge TTS does need internet for synthesis
        public bool RequiresApiKey => false;

        public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
        {
            if (_checkedAvailability) return _isAvailable;

            // Try both 'python' and 'py' commands since different systems have different setups
            string[] pythonCommands = { "python", "py", "python3" };
            
            foreach (var pythonCmd in pythonCommands)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = pythonCmd,
                        Arguments = "-m edge_tts --version",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using var process = Process.Start(psi);
                    if (process != null)
                    {
                        await process.WaitForExitAsync(ct);
                        if (process.ExitCode == 0)
                        {
                            _isAvailable = true;
                            _pythonCommand = pythonCmd; // Remember which command worked
                            Debug.WriteLine($"[EdgeTTS] Available using '{pythonCmd}'");
                            break;
                        }
                    }
                }
                catch
                {
                    // Try next command
                }
            }

            _checkedAvailability = true;
            return _isAvailable;
        }
        
        private string _pythonCommand = "python"; // Default, updated by IsAvailableAsync

        public Task<IReadOnlyList<VoiceInfo>> GetVoicesAsync(CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<VoiceInfo>>(BuiltInVoices);
        }

        public async Task<SynthesisResult> SynthesizeAsync(string text, SynthesisOptions options, CancellationToken ct = default)
        {
            if (!await IsAvailableAsync(ct))
            {
                return new SynthesisResult
                {
                    Success = false,
                    ErrorMessage = "Edge TTS not available. Install with: pip install edge-tts"
                };
            }

            _currentCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var audioFile = Path.Combine(Path.GetTempPath(), $"atlas_edge_{Guid.NewGuid()}.mp3");
            var textFile = Path.Combine(Path.GetTempPath(), $"atlas_edge_{Guid.NewGuid()}.txt");

            try
            {
                // Write text to a file to avoid command line escaping issues
                // Clean the text first - remove problematic characters
                var cleanText = text
                    .Replace("\r\n", " ")
                    .Replace("\n", " ")
                    .Replace("\r", " ")
                    .Replace("\"", "'")
                    .Trim();
                
                // Write to temp file (edge-tts can read from file with -f flag)
                await File.WriteAllTextAsync(textFile, cleanText, ct);
                
                // Build rate argument (edge-tts uses percentage: +0% is normal, +50% is 1.5x)
                var ratePercent = (int)((options.Rate - 1.0) * 100);
                var rateArg = ratePercent >= 0 ? $"+{ratePercent}%" : $"{ratePercent}%";

                var psi = new ProcessStartInfo
                {
                    FileName = _pythonCommand, // Use the Python command that worked in IsAvailableAsync
                    Arguments = $"-m edge_tts --voice \"{options.VoiceId}\" --rate=\"{rateArg}\" -f \"{textFile}\" --write-media \"{audioFile}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                Debug.WriteLine($"[EdgeTTS] Running: {_pythonCommand} {psi.Arguments}");
                
                _currentProcess = Process.Start(psi);
                if (_currentProcess != null)
                {
                    await _currentProcess.WaitForExitAsync(_currentCts.Token);
                    
                    // Clean up text file
                    try { File.Delete(textFile); } catch { }

                    if (_currentProcess.ExitCode == 0 && File.Exists(audioFile))
                    {
                        return new SynthesisResult
                        {
                            Success = true,
                            AudioFilePath = audioFile
                        };
                    }
                    else
                    {
                        var error = await _currentProcess.StandardError.ReadToEndAsync();
                        return new SynthesisResult
                        {
                            Success = false,
                            ErrorMessage = $"Edge TTS failed: {error}"
                        };
                    }
                }

                return new SynthesisResult { Success = false, ErrorMessage = "Failed to start Edge TTS process" };
            }
            catch (OperationCanceledException)
            {
                return new SynthesisResult { Success = false, ErrorMessage = "Synthesis cancelled" };
            }
            catch (Exception ex)
            {
                return new SynthesisResult { Success = false, ErrorMessage = ex.Message };
            }
            finally
            {
                _currentProcess = null;
                _currentCts = null;
            }
        }

        public void CancelCurrentSpeech()
        {
            try
            {
                _currentCts?.Cancel();
                if (_currentProcess != null && !_currentProcess.HasExited)
                {
                    _currentProcess.Kill();
                }
            }
            catch { }
        }

        public void Configure(Dictionary<string, string> settings)
        {
            // Edge TTS doesn't require configuration
        }
    }
}
