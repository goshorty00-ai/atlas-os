using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AtlasAI.Views.ViewModels;
using AtlasAI.Voice;

namespace AtlasAI.UI.Pages
{
    public partial class SecurityPage : UserControl
    {
        private VoiceManager? _voiceManager;
        private SecurityScannerViewModel? _vm;

        public event EventHandler<string>? ChatMessageSent;

        public SecurityPage()
        {
            InitializeComponent();
            Loaded += SecurityPage_Loaded;
            Unloaded += SecurityPage_Unloaded;
        }

        private void SecurityPage_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            _vm ??= new SecurityScannerViewModel();
            DataContext = _vm;
            _vm.Start();
            HookChat();
        }

        private void SecurityPage_Unloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            try { _vm?.Stop(); } catch { }
        }

        private void HookChat()
        {
            try
            {
                if (_vm == null) return;
                _vm.Chat.UserMessageSent -= Chat_UserMessageSent;
                _vm.Chat.UserMessageSent += Chat_UserMessageSent;
            }
            catch
            {
            }
        }

        private void Chat_UserMessageSent(object? sender, string message)
        {
            try { } catch { }
        }

        private void RestoreShellChrome_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Window.GetWindow(this) is AtlasAI.ChatWindow chatWindow)
                    chatWindow.RestoreShellChromeAndHeader();
            }
            catch
            {
            }
        }

        public void SetVoiceManager(VoiceManager voiceManager) => _voiceManager = voiceManager;

        public Task StartScanAsync()
        {
            try
            {
                _vm ??= new SecurityScannerViewModel();
                DataContext = _vm;
                _vm.Start();
            }
            catch
            {
            }
            return Task.CompletedTask;
        }

        public void ResetScan()
        {
            try
            {
                _vm?.Stop();
                _vm = new SecurityScannerViewModel();
                DataContext = _vm;
                _vm.Start();
            }
            catch
            {
            }
        }
    }
}
