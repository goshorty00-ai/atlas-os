using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AtlasAI.UI
{
    /// <summary>
    /// Toast notification types for different visual styles
    /// </summary>
    public enum ToastType
    {
        Info,
        Success,
        Warning,
        Error
    }

    /// <summary>
    /// Non-blocking toast notification system for Atlas AI
    /// Displays notifications in the bottom-right corner with auto-dismiss
    /// </summary>
    public class ToastNotificationManager
    {
        private static ToastNotificationManager? _instance;
        private StackPanel? _container;
        private readonly Queue<ToastRequest> _queue = new();
        private const int MaxVisible = 3;
        private int _visibleCount = 0;

        public static ToastNotificationManager Instance => _instance ??= new ToastNotificationManager();

        /// <summary>
        /// Initialize the toast system with a container panel
        /// </summary>
        public void Initialize(StackPanel container)
        {
            _container = container;
        }

        /// <summary>
        /// Show a toast notification
        /// </summary>
        public void Show(string message, ToastType type = ToastType.Info, int durationMs = 3000)
        {
            if (_container == null) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                EnqueueOrShow(new ToastRequest
                {
                    CreateToast = () => CreateToast(message, type),
                    DurationMs = durationMs
                });
            });
        }

        public void ShowAction(string message, string primaryText, Action primaryAction, string secondaryText, Action? secondaryAction = null, ToastType type = ToastType.Info, int durationMs = 8000)
        {
            if (_container == null) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                EnqueueOrShow(new ToastRequest
                {
                    CreateToast = () => CreateActionToast(message, primaryText, primaryAction, secondaryText, secondaryAction, type),
                    DurationMs = durationMs
                });
            });
        }

        /// <summary>
        /// Show success toast
        /// </summary>
        public void ShowSuccess(string message, int durationMs = 3000)
            => Show(message, ToastType.Success, durationMs);

        /// <summary>
        /// Show warning toast
        /// </summary>
        public void ShowWarning(string message, int durationMs = 4000)
            => Show(message, ToastType.Warning, durationMs);

        /// <summary>
        /// Show error toast
        /// </summary>
        public void ShowError(string message, int durationMs = 5000)
            => Show(message, ToastType.Error, durationMs);

        private Border CreateToast(string message, ToastType type)
        {
            var (icon, borderColor) = type switch
            {
                ToastType.Success => ("✓", Color.FromRgb(63, 185, 80)),
                ToastType.Warning => ("⚠", Color.FromRgb(210, 153, 34)),
                ToastType.Error => ("✕", Color.FromRgb(248, 81, 73)),
                _ => ("ℹ", Color.FromRgb(0, 212, 255))
            };

            var toast = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(28, 33, 40)),
                BorderBrush = new SolidColorBrush(borderColor),
                BorderThickness = new Thickness(1, 1, 1, 1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 8),
                MinWidth = 200,
                MaxWidth = 320,
                Opacity = 0,
                RenderTransform = new TranslateTransform(50, 0),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 16,
                    ShadowDepth = 4,
                    Opacity = 0.3
                }
            };

            var content = new StackPanel { Orientation = Orientation.Horizontal };
            
            // Icon
            content.Children.Add(new TextBlock
            {
                Text = icon,
                FontSize = 14,
                Foreground = new SolidColorBrush(borderColor),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            });

            // Message
            content.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(230, 237, 243)),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 260,
                VerticalAlignment = VerticalAlignment.Center
            });

            toast.Child = content;
            return toast;
        }

        private async void ShowToast(Border toast, int durationMs)
        {
            if (_container == null) return;

            _visibleCount++;
            _container.Children.Insert(0, toast);

            // Slide in animation
            var slideIn = new DoubleAnimation(50, 0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));

            toast.RenderTransform.BeginAnimation(TranslateTransform.XProperty, slideIn);
            toast.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            // Wait for duration
            await Task.Delay(durationMs);

            if (_container == null) return;
            if (!_container.Children.Contains(toast)) return;

            // Slide out animation
            var slideOut = new DoubleAnimation(0, 50, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));

            toast.RenderTransform.BeginAnimation(TranslateTransform.XProperty, slideOut);
            toast.BeginAnimation(UIElement.OpacityProperty, fadeOut);

            await Task.Delay(200);

            DismissToast(toast);
        }

        private void EnqueueOrShow(ToastRequest request)
        {
            if (_container == null) return;

            if (_visibleCount >= MaxVisible)
            {
                _queue.Enqueue(request);
                return;
            }

            var toast = request.CreateToast();
            ShowToast(toast, request.DurationMs);
        }

        private void DismissToast(Border toast)
        {
            if (_container == null) return;
            if (_container.Children.Contains(toast))
                _container.Children.Remove(toast);

            if (_visibleCount > 0)
                _visibleCount--;

            if (_queue.Count > 0)
            {
                var next = _queue.Dequeue();
                EnqueueOrShow(next);
            }
        }

        private Border CreateActionToast(string message, string primaryText, Action primaryAction, string secondaryText, Action? secondaryAction, ToastType type)
        {
            var (icon, borderColor) = type switch
            {
                ToastType.Success => ("✓", Color.FromRgb(63, 185, 80)),
                ToastType.Warning => ("⚠", Color.FromRgb(210, 153, 34)),
                ToastType.Error => ("✕", Color.FromRgb(248, 81, 73)),
                _ => ("ℹ", Color.FromRgb(0, 212, 255))
            };

            var toast = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(28, 33, 40)),
                BorderBrush = new SolidColorBrush(borderColor),
                BorderThickness = new Thickness(1, 1, 1, 1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 8),
                MinWidth = 240,
                MaxWidth = 360,
                Opacity = 0,
                RenderTransform = new TranslateTransform(50, 0),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 16,
                    ShadowDepth = 4,
                    Opacity = 0.3
                }
            };

            var root = new StackPanel { Orientation = Orientation.Vertical };

            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(new TextBlock
            {
                Text = icon,
                FontSize = 14,
                Foreground = new SolidColorBrush(borderColor),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            header.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(230, 237, 243)),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 300,
                VerticalAlignment = VerticalAlignment.Center
            });

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var primaryBtn = new Button
            {
                Content = primaryText,
                Background = new SolidColorBrush(Color.FromRgb(34, 211, 238)),
                Foreground = new SolidColorBrush(Color.FromRgb(10, 14, 18)),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(12, 6, 12, 6),
                MinWidth = 92
            };
            var secondaryBtn = new Button
            {
                Content = secondaryText,
                Background = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(12, 6, 12, 6),
                MinWidth = 92,
                Margin = new Thickness(8, 0, 0, 0)
            };

            primaryBtn.Click += (_, __) =>
            {
                try { primaryAction(); } catch { }
                DismissToast(toast);
            };

            secondaryBtn.Click += (_, __) =>
            {
                try { secondaryAction?.Invoke(); } catch { }
                DismissToast(toast);
            };

            buttons.Children.Add(primaryBtn);
            buttons.Children.Add(secondaryBtn);

            root.Children.Add(header);
            root.Children.Add(buttons);

            toast.Child = root;
            return toast;
        }

        private sealed class ToastRequest
        {
            public Func<Border> CreateToast { get; set; } = null!;
            public int DurationMs { get; set; }
        }
    }
}
