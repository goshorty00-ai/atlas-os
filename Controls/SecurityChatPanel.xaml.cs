using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AtlasAI.Controls
{
    public partial class SecurityChatPanel : UserControl
    {
        public static readonly DependencyProperty MonoFontFamilyProperty =
            DependencyProperty.Register(nameof(MonoFontFamily), typeof(FontFamily), typeof(SecurityChatPanel),
                new PropertyMetadata(new FontFamily("Cascadia Code, Consolas")));

        public FontFamily MonoFontFamily
        {
            get => (FontFamily)GetValue(MonoFontFamilyProperty);
            set => SetValue(MonoFontFamilyProperty, value);
        }

        public SecurityChatPanel()
        {
            InitializeComponent();
            Loaded += SecurityChatPanel_Loaded;
            Unloaded += SecurityChatPanel_Unloaded;
        }

        private void SecurityChatPanel_Loaded(object sender, RoutedEventArgs e)
        {
            TryHookMessages();
            ScrollToEnd();
        }

        private void SecurityChatPanel_Unloaded(object sender, RoutedEventArgs e)
        {
            TryUnhookMessages();
        }

        private INotifyCollectionChanged? _hooked;

        private void TryHookMessages()
        {
            try
            {
                TryUnhookMessages();
                if (DataContext == null) return;
                var prop = DataContext.GetType().GetProperty("Messages");
                var val = prop?.GetValue(DataContext) as INotifyCollectionChanged;
                if (val == null) return;
                _hooked = val;
                _hooked.CollectionChanged += Messages_CollectionChanged;
            }
            catch
            {
            }
        }

        private void TryUnhookMessages()
        {
            try
            {
                if (_hooked != null)
                    _hooked.CollectionChanged -= Messages_CollectionChanged;
            }
            catch
            {
            }
            finally
            {
                _hooked = null;
            }
        }

        private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            ScrollToEnd();
        }

        private void ScrollToEnd()
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { MessagesScroll.ScrollToEnd(); } catch { }
                }));
            }
            catch
            {
            }
        }

        private void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (e.Key != Key.Enter) return;
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift) return;
                e.Handled = true;

                var prop = DataContext?.GetType().GetProperty("SendCommand");
                if (prop?.GetValue(DataContext) is ICommand cmd)
                {
                    if (cmd.CanExecute(null)) cmd.Execute(null);
                }
            }
            catch
            {
            }
        }

        private void InputArea_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (InputBox == null) return;
                InputBox.Focus();
                Keyboard.Focus(InputBox);
                InputBox.CaretIndex = InputBox.Text?.Length ?? 0;
            }
            catch
            {
            }
        }
    }
}
