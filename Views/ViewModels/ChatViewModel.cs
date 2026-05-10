using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;
using AtlasAI.AI;
using AtlasAI.Voice;

namespace AtlasAI.Views.ViewModels
{
    public sealed class ChatViewModel : INotifyPropertyChanged
    {
        private readonly VoiceManager? _voiceManager;
        private bool _isSending;
        private bool _isTyping;
        private bool _isRecording;
        private string _inputText = "";
        private string _voiceStatusText = "";
        private DateTime _voiceStatusHoldUntilUtc = DateTime.MinValue;
        private System.Threading.CancellationTokenSource? _currentCts;

        public ObservableCollection<AtlasAI.Models.ChatMessage> Messages { get; } = new();
        public ObservableCollection<string> Attachments { get; } = new();

        public bool HasMessages => Messages.Count > 0;
        public bool HasAttachments => Attachments.Count > 0;

        public string VoiceStatusText
        {
            get => _voiceStatusText;
            private set => SetProperty(ref _voiceStatusText, value);
        }

        public bool IsRecording
        {
            get => _isRecording;
            set => SetProperty(ref _isRecording, value);
        }

        public bool IsTyping
        {
            get => _isTyping;
            private set
            {
                if (SetProperty(ref _isTyping, value))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        public string InputText
        {
            get => _inputText;
            set
            {
                if (SetProperty(ref _inputText, value))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        public ICommand SendCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ToggleRecordingCommand { get; }
        public ICommand TestVoiceCommand { get; }

        public ChatViewModel(VoiceManager? voiceManager)
        {
            _voiceManager = voiceManager;
            SendCommand = new RelayCommand(SendAsync, CanSend);
            CancelCommand = new RelayCommand(CancelOperation, () => _isSending);
            ToggleRecordingCommand = new RelayCommand(ToggleRecording);
            TestVoiceCommand = new RelayCommand(async () =>
            {
                try
                {
                    if (_voiceManager == null) return;
                    if (!_voiceManager.SpeechEnabled) _voiceManager.SpeechEnabled = true;
                    if (_voiceManager.Volume < 0.05) _voiceManager.Volume = 1.0;
                    VoiceStatusText = $"Voice test · {_voiceManager.ActiveProviderType} · vol {_voiceManager.Volume:0.00}";
                    await _voiceManager.SpeakAsync(new AssistantUtterance("Audio online.", UtteranceSource.Conversation));
                    VoiceStatusText = "";
                }
                catch (Exception ex)
                {
                    VoiceStatusText = ex.Message;
                }
            });

            if (_voiceManager != null)
            {
                try
                {
                    _voiceManager.SpeechError += (_, msg) =>
                    {
                        try
                        {
                            OnUi(() => SetVoiceStatus(msg));
                        }
                        catch
                        {
                        }
                    };
                }
                catch
                {
                }
            }

            // Hook up Voice Orchestrator
            VoiceSystemOrchestrator.Instance.ListeningStarted += (s, e) =>
            {
                OnUi(() =>
                {
                    IsRecording = true;
                    TryClearVoiceStatus();
                });
            };
            VoiceSystemOrchestrator.Instance.ListeningStopped += (s, e) =>
            {
                OnUi(() =>
                {
                    IsRecording = false;
                    if (string.Equals(VoiceStatusText, "Listening...", StringComparison.OrdinalIgnoreCase))
                        TryClearVoiceStatus();
                });
            };
            VoiceSystemOrchestrator.Instance.Error += (s, msg) =>
            {
                OnUi(() => SetVoiceStatus(msg));
            };

            // Register PushToTalkCommandHandler but check if it's already set (e.g. by MediaCentre)
            var orchestrator = VoiceSystemOrchestrator.Instance;
            if (orchestrator.PushToTalkCommandHandler == null)
            {
                orchestrator.PushToTalkCommandHandler = (text) =>
                {
                    OnUi(() =>
                    {
                        InputText = text;
                        TryClearVoiceStatus();
                        
                        // Auto-send the captured voice command
                        if (SendCommand.CanExecute(null))
                        {
                            SendCommand.Execute(null);
                        }
                    });
                };
            }

            // Prefer the unified SubmitMessageHandler when available.
            // This covers wake-word flows that otherwise display "Processing..." but never reach the chat send pipeline.
            if (orchestrator.SubmitMessageHandler == null)
            {
	            orchestrator.SubmitMessageHandler = (text) =>
	            {
	            	OnUi(() =>
	            	{
	            		InputText = text;
	            		TryClearVoiceStatus();
	            		if (SendCommand.CanExecute(null))
	            			SendCommand.Execute(null);
	            	});
	            };
            }

            Messages.CollectionChanged += Messages_CollectionChanged;
            Attachments.CollectionChanged += Attachments_CollectionChanged;
        }

        private static void OnUi(Action a)
        {
            try
            {
                if (a == null) return;
                var app = System.Windows.Application.Current;
                if (app?.Dispatcher == null)
                {
                    a();
                    return;
                }

                if (app.Dispatcher.CheckAccess())
                {
                    a();
                    return;
                }

                app.Dispatcher.BeginInvoke(a);
            }
            catch
            {
            }
        }

        private void SetVoiceStatus(string? text)
        {
            var t = (text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(t))
            {
                TryClearVoiceStatus();
                return;
            }

            var now = DateTime.UtcNow;
            var isImportant = IsWarningOrErrorText(t);
            var isProcessing = t.StartsWith("Processing", StringComparison.OrdinalIgnoreCase) ||
                               t.StartsWith("💭 Processing", StringComparison.OrdinalIgnoreCase);

            // If we're holding an important message, don't overwrite it with less important updates.
            if (_voiceStatusHoldUntilUtc > now && IsWarningOrErrorText(VoiceStatusText) && !isImportant)
                return;

            VoiceStatusText = t;

            if (isImportant)
                _voiceStatusHoldUntilUtc = now.AddSeconds(12);
            else if (isProcessing)
                _voiceStatusHoldUntilUtc = now.AddSeconds(3);
        }

        private void TryClearVoiceStatus()
        {
            var now = DateTime.UtcNow;
            if (_voiceStatusHoldUntilUtc > now && IsWarningOrErrorText(VoiceStatusText))
                return;

            VoiceStatusText = "";
        }

        private static bool IsWarningOrErrorText(string? text)
        {
            var t = (text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(t)) return false;

            if (t.StartsWith("⚠", StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith("❌", StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith("WARN", StringComparison.OrdinalIgnoreCase))
                return true;

            // Heuristics to keep common voice failures visible.
            return t.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                   t.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                   t.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                   t.Contains("no speech", StringComparison.OrdinalIgnoreCase) ||
                   t.Contains("no_speech", StringComparison.OrdinalIgnoreCase) ||
                   t.Contains("canceled", StringComparison.OrdinalIgnoreCase) ||
                   t.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) ||
                   t.Contains("quota", StringComparison.OrdinalIgnoreCase) ||
                   t.Contains("network", StringComparison.OrdinalIgnoreCase) ||
                   t.Contains("mic", StringComparison.OrdinalIgnoreCase) ||
                   t.Contains("microphone", StringComparison.OrdinalIgnoreCase);
        }

        private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(HasMessages));
        }

        private void Attachments_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(HasAttachments));
            CommandManager.InvalidateRequerySuggested();
        }

        private bool CanSend()
        {
            if (_isSending) return false;
            if (IsTyping) return false;
            return !string.IsNullOrWhiteSpace(InputText) || Attachments.Count > 0;
        }

        private void ToggleRecording()
        {
            if (VoiceSystemOrchestrator.Instance.IsListening)
            {
                VoiceSystemOrchestrator.Instance.StopListening();
            }
            else
            {
                VoiceSystemOrchestrator.Instance.BeginListening(ListeningSource.PushToTalk);
            }
        }

        private void CancelOperation()
        {
            _currentCts?.Cancel();
            _currentCts?.Dispose();
            _currentCts = null;
        }

        private async void SendAsync()
        {
            var text = (InputText ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text) && Attachments.Count == 0) return;

            _isSending = true;
            _currentCts = new System.Threading.CancellationTokenSource();
            CommandManager.InvalidateRequerySuggested();

            try
            {
                var combinedText = BuildUserPayload(text, Attachments.ToList());

                Messages.Add(new AtlasAI.Models.ChatMessage
                {
                    Content = combinedText,
                    IsUser = true,
                    Timestamp = DateTime.Now
                });

                InputText = "";
                Attachments.Clear();

                IsTyping = true;

                // Combined cancellation: user + 120s timeout
                using var timeoutCts = new System.Threading.CancellationTokenSource(120000);
                using var linkedCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(_currentCts.Token, timeoutCts.Token);
                var ct = linkedCts.Token;

                await Task.Delay(450, ct);

                if (TryParseMediaCentreQuery(combinedText, out var searchQuery, out var typeId))
                {
                    var vm = MediaCentreViewModel.Instance;
                    var lines = vm != null
                        ? (await Task.Run(() => vm.FindLibraryItems(searchQuery, typeId, 500), ct))
                            .Select(r =>
                            {
                                var t = (r.Title ?? "").Trim();
                                if (!string.IsNullOrWhiteSpace(t)) return t;
                                var p = (r.FilePath ?? "").Trim();
                                if (string.IsNullOrWhiteSpace(p)) return "";
                                var n = Path.GetFileNameWithoutExtension(p) ?? "";
                                return (n ?? "").Trim();
                            })
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList()
                        : await Task.Run(() => MediaCentreViewModel.FindPersistedLibraryTitles(searchQuery, typeId, 500), ct);

                    var label = string.IsNullOrWhiteSpace(typeId) ? "ITEMS" : typeId.ToUpperInvariant();
                    var qLabel = string.IsNullOrWhiteSpace(searchQuery) ? "(no query)" : searchQuery;

                    var localAssistantText =
                        lines.Count == 0
                            ? $"NO MATCHES · {label} · {qLabel}"
                            : $"FOUND {lines.Count} · {label} · {qLabel}\n" + string.Join("\n", lines.Select(t => $"• {t}"));

                    IsTyping = false;
                    Messages.Add(new AtlasAI.Models.ChatMessage
                    {
                        Content = localAssistantText,
                        IsUser = false,
                        Timestamp = DateTime.Now
                    });

                    if (_voiceManager != null)
                        _ = TrySpeakAsync(lines.Count == 0 ? "No matches found." : $"Found {lines.Count}.");
                    return;
                }

                var context = BuildContext(combinedText);
                var response = await AIManager.SendMessageAsync(context, 800, ct);
                var assistantText = response.Success ? response.Content : response.Error;
                if (string.IsNullOrWhiteSpace(assistantText))
                    assistantText = "NO RESPONSE · RETRY";

                IsTyping = false;

                Messages.Add(new AtlasAI.Models.ChatMessage
                {
                    Content = assistantText,
                    IsUser = false,
                    Timestamp = DateTime.Now
                });

                if (response.Success && _voiceManager != null)
                    _ = TrySpeakAsync(assistantText);
            }
            catch (OperationCanceledException)
            {
                IsTyping = false;
                Messages.Add(new AtlasAI.Models.ChatMessage
                {
                    Content = "CANCELLED · OPERATION STOPPED",
                    IsUser = false,
                    Timestamp = DateTime.Now
                });
            }
            catch
            {
                IsTyping = false;
                Messages.Add(new AtlasAI.Models.ChatMessage
                {
                    Content = "ERROR · REQUEST FAILED",
                    IsUser = false,
                    Timestamp = DateTime.Now
                });
            }
            finally
            {
                _isSending = false;
                _currentCts?.Dispose();
                _currentCts = null;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private static bool TryParseMediaCentreQuery(string text, out string query, out string? typeId)
        {
            query = "";
            typeId = null;

            var raw = (text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(raw)) return false;

            var lower = raw.ToLowerInvariant();
            if (!lower.Contains("media centre") && !lower.Contains("media center") && !lower.Contains("media centr")) return false;
            if (!lower.Contains("find") && !lower.Contains("search") && !lower.Contains("list") && !lower.Contains("show")) return false;

            if (lower.Contains("movie"))
                typeId = "movies";
            else if (lower.Contains("tv") || lower.Contains("series") || lower.Contains("show"))
                typeId = "tv";
            else if (lower.Contains("music") || lower.Contains("song") || lower.Contains("track"))
                typeId = "music";

            query = raw;

            query = Regex.Replace(query, "\\b(show me|show|list|find|search|in|inside|me|the)\\b", " ", RegexOptions.IgnoreCase);
            query = Regex.Replace(query, "\\b(media centre|media center|media centr|media)\\b", " ", RegexOptions.IgnoreCase);
            query = Regex.Replace(query, "\\b(movies|movie|tv|shows|show|series|music|songs|song|tracks|track)\\b", " ", RegexOptions.IgnoreCase);
            query = Regex.Replace(query, "\\b(all|every)\\b", " ", RegexOptions.IgnoreCase);
            query = Regex.Replace(query, "\\s+", " ").Trim();

            return true;
        }

        public void TrySend()
        {
            if (_isSending) return;
            if (IsTyping) return;

            var text = (InputText ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text) && Attachments.Count == 0) return;

            SendAsync();
        }

        public void AddAttachments(IEnumerable<string> paths)
        {
            foreach (var p in paths)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                if (Attachments.Contains(p)) continue;
                Attachments.Add(p);
            }
        }

        public void RemoveAttachment(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            Attachments.Remove(path);
        }

        private static string BuildUserPayload(string text, List<string> attachments)
        {
            if (attachments.Count == 0) return text;

            var normalizedText = string.IsNullOrWhiteSpace(text) ? "(no message)" : text;
            var parts = new List<string> { normalizedText };

            foreach (var path in attachments)
            {
                var fileName = Path.GetFileName(path);
                if (string.IsNullOrWhiteSpace(fileName)) fileName = path;

                try
                {
                    if (File.Exists(path))
                    {
                        var ext = Path.GetExtension(path).ToLowerInvariant();
                        var isText =
                            ext is ".txt" or ".md" or ".cs" or ".json" or ".xml" or ".xaml" or ".yml" or ".yaml" or
                            ".js" or ".ts" or ".tsx" or ".jsx" or ".py" or ".java" or ".kt" or ".cpp" or ".h" or ".hpp" or
                            ".css" or ".scss" or ".html" or ".htm";

                        var info = new FileInfo(path);
                        if (isText && info.Length <= 50_000)
                        {
                            var content = File.ReadAllText(path);
                            parts.Add($"[Attachment: {fileName}]\n{content}");
                            continue;
                        }
                    }
                }
                catch
                {
                }

                parts.Add($"[Attachment: {fileName}]");
            }

            return string.Join("\n\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        private List<object> BuildContext(string latestUserMessage)
        {
            var context = new List<object>
            {
                new
                {
                    role = "system",
                    content =
                        "You are Atlas AI inside a futuristic command interface and developer workstation. " +
                        "You CAN help the user build apps: propose designs, write code, generate project structures, and provide exact build/run commands. " +
                        "If you cannot directly execute something, give the command anyway and continue by reasoning from the expected output. " +
                        "Use brief, authoritative, system-style responses. " +
                        "Prefer short status lines like: 'ACKNOWLEDGED · PROCESSING', 'TASK COMPLETE', 'AWAITING INPUT'."
                }
            };

            foreach (var msg in Messages.TakeLast(16))
            {
                if (string.IsNullOrWhiteSpace(msg.Content)) continue;
                context.Add(new { role = msg.IsUser ? "user" : "assistant", content = msg.Content });
            }

            return context;
        }

        private async Task TrySpeakAsync(string text)
        {
            try
            {
                if (_voiceManager == null) return;
                if (_voiceManager.Volume < 0.05) _voiceManager.Volume = 1.0;
                if (!_voiceManager.SpeechEnabled) _voiceManager.SpeechEnabled = true;
                await _voiceManager.SpeakAsync(text, Voice.ResponseType.Normal);
            }
            catch
            {
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
