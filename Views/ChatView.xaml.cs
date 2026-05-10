using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Threading;
using AtlasAI.AI;
using AtlasAI.Voice;
using AtlasAI.Views.ViewModels;
using Microsoft.Win32;

namespace AtlasAI.Views
{
    public partial class ChatView : UserControl
    {
        private readonly ChatViewModel _viewModel;

        public ChatView() : this(null)
        {
        }

        public ChatView(VoiceManager? voiceManager)
        {
            InitializeComponent();

            _viewModel = new ChatViewModel(voiceManager);
            DataContext = _viewModel;

            _viewModel.Messages.CollectionChanged += Messages_CollectionChanged;
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;

            Loaded += ChatView_Loaded;
        }

        private void ChatView_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // ChatView no longer owns an orb; ChatWindow hosts the production orb.
                // Ensure any legacy references are hidden.
                var overlay = this.FindName("OrbOverlay") as UIElement;
                if (overlay != null) overlay.Visibility = Visibility.Collapsed;

                // Voice UI sync disabled when orb is removed
            }
            catch
            {
            }
        }

        public void ApplyOrbStyle(bool useLottie, string? animationFile = null)
        {
            return;
        }

        private DispatcherTimer? _voiceTimer;
        private DateTime _lastTickTime;
        private void VoiceTimer_Tick(object? sender, EventArgs e)
        {
            return;
        }

        protected override void OnVisualParentChanged(DependencyObject oldParent)
        {
            base.OnVisualParentChanged(oldParent);
            if (VisualParent == null && _voiceTimer != null)
            {
                try { _voiceTimer.Stop(); } catch { }
                _voiceTimer = null;
            }
        }

        private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            ScrollToBottomSoon();
        }

        

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChatViewModel.IsTyping))
                ScrollToBottomSoon();
        }

        private void ScrollToBottomSoon()
        {
            try
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ScrollToBottom));
            }
            catch
            {
            }
        }

        private void ScrollToBottom()
        {
            try
            {
                MessageScrollViewer?.ScrollToEnd();
            }
            catch
            {
            }
        }

        private void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (Keyboard.Modifiers != ModifierKeys.None) return;

            e.Handled = true;
            _viewModel.TrySend();
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.TrySend();
        }

        private void AttachButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Multiselect = true,
                    Title = "Select attachments"
                };

                if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

                _viewModel.AddAttachments(dialog.FileNames.Where(f => !string.IsNullOrWhiteSpace(f)));
            }
            catch
            {
            }
        }

        private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not FrameworkElement fe) return;
                if (fe.Tag is not string path) return;
                _viewModel.RemoveAttachment(path);
            }
            catch
            {
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var owner = Window.GetWindow(this);
                var window = new global::AtlasAI.SettingsWindow();
                if (owner != null) window.Owner = owner;
                window.Show();
            }
            catch
            {
            }
        }

        private void AiModeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var owner = Window.GetWindow(this);
                var window = new AiModeWindow();
                if (owner != null) window.Owner = owner;
                window.Show();
            }
            catch
            {
            }
        }

        private void VoiceModeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var owner = Window.GetWindow(this);
                var window = new VoiceModeWindow();
                if (owner != null) window.Owner = owner;
                window.Show();
            }
            catch
            {
            }
        }
    }
}
