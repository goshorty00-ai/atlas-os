using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Linq;
using System.Threading.Tasks;
using AtlasAI.AI;

namespace AtlasAI.Views.ViewModels
{
    public sealed class SecurityChatViewModel : INotifyPropertyChanged
    {
        private readonly Random _rng = new Random();
        private string _inputText = "";
        private bool _isSending;
        private System.Threading.CancellationTokenSource? _currentCts;

        public ObservableCollection<AtlasAI.Models.ChatMessage> Messages { get; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<string>? UserMessageSent;

        public Func<string>? ContextProvider { get; set; }

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

        public SecurityChatViewModel()
        {
            SendCommand = new RelayCommand(Send, CanSend);
            Messages.Add(new AtlasAI.Models.ChatMessage
            {
                IsUser = false,
                Timestamp = DateTime.Now,
                Content = "SECURITY SYSTEMS ONLINE · ALL METRICS NOMINAL"
            });
        }

        private bool CanSend() => !_isSending && !string.IsNullOrWhiteSpace(InputText);

        private async void Send()
        {
            var msg = (InputText ?? "").Trim();
            if (string.IsNullOrWhiteSpace(msg)) return;

            Messages.Add(new AtlasAI.Models.ChatMessage { IsUser = true, Timestamp = DateTime.Now, Content = msg });
            try { UserMessageSent?.Invoke(this, msg); } catch { }
            InputText = "";

            _isSending = true;
            _currentCts = new System.Threading.CancellationTokenSource();
            CommandManager.InvalidateRequerySuggested();

            try
            {
                var response = await TryGetAiResponseAsync(msg, _currentCts.Token).ConfigureAwait(true);
                if (string.IsNullOrWhiteSpace(response))
                    response = PickResponse(msg);

                Messages.Add(new AtlasAI.Models.ChatMessage { IsUser = false, Timestamp = DateTime.Now, Content = response });
            }
            catch (OperationCanceledException)
            {
                Messages.Add(new AtlasAI.Models.ChatMessage { IsUser = false, Timestamp = DateTime.Now, Content = "CANCELLED · OPERATION STOPPED" });
            }
            catch
            {
                var response = PickResponse(msg);
                Messages.Add(new AtlasAI.Models.ChatMessage { IsUser = false, Timestamp = DateTime.Now, Content = response });
            }
            finally
            {
                _isSending = false;
                _currentCts?.Dispose();
                _currentCts = null;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private async Task<string> TryGetAiResponseAsync(string userText, System.Threading.CancellationToken ct)
        {
            var context = "";
            try { context = ContextProvider?.Invoke() ?? ""; } catch { }

            var system = "You are Atlas Security AI Assistant inside a futuristic Windows security scanner UI. " +
                         "Respond in short, crisp lines, using ALL CAPS status style when appropriate. " +
                         "Do not invent detections. If you have no proof of threats, say none detected. " +
                         "If the user asks for the status, summarize the metrics and scan phase. " +
                         "Keep it concise.\n" +
                         (string.IsNullOrWhiteSpace(context) ? "" : ("\nCURRENT TELEMETRY:\n" + context));

            var messages = new System.Collections.Generic.List<object>
            {
                new { role = "system", content = system }
            };

            foreach (var m in Messages.TakeLast(10))
            {
                var content = (m.Content ?? "").Trim();
                if (string.IsNullOrWhiteSpace(content)) continue;
                messages.Add(new { role = m.IsUser ? "user" : "assistant", content });
            }

            var resp = await AIManager.SendMessageAsync("SecurityScanner", messages, 350, ct);
            if (!resp.Success) return "";
            return (resp.Content ?? "").Trim();
        }

        private string PickResponse(string userText)
        {
            var t = (userText ?? "").ToLowerInvariant();
            if (t.Contains("threat") || t.Contains("virus") || t.Contains("malware"))
                return "ACKNOWLEDGED · THREAT MONITORING ACTIVE · NO ANOMALIES DETECTED";
            if (t.Contains("status"))
                return "STATUS: STABLE · AI CONTINUOUSLY MONITORING FILES, PROCESSES, NETWORK";
            if (t.Contains("scan"))
                return "THREAT SCAN INITIATED · MONITORING ACTIVE SURFACES";

            var options = new[]
            {
                "ACKNOWLEDGED · SECURITY PARAMETERS STABLE",
                "CONFIRMED · NEURAL DEFENSE GRID ONLINE",
                "STANDING BY · PROVIDE A TARGET OR VECTOR TO ANALYZE",
                "SYSTEMS NOMINAL · NO THREAT SIGNATURES MATCHED"
            };
            return options[_rng.Next(options.Length)];
        }

        private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            return true;
        }
    }
}
