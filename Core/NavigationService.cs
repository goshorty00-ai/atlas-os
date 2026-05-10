using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AtlasAI.Core
{
    /// <summary>
    /// Minimal in-app navigation service for a WPF shell.
    /// - RegisterRoute(key, factory)
    /// - Navigate(key)
    /// - CurrentView (bind in XAML)
    /// No external packages.
    /// </summary>
    public sealed class NavigationService : INotifyPropertyChanged
    {
        private readonly Dictionary<string, Func<object>> _routes = new(StringComparer.OrdinalIgnoreCase);

        private object? _currentView;
        public object? CurrentView
        {
            get => _currentView;
            private set
            {
                if (!ReferenceEquals(_currentView, value))
                {
                    _currentView = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void RegisterRoute(string key, Func<object> factory)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Route key cannot be null/empty.", nameof(key));
            if (factory is null)
                throw new ArgumentNullException(nameof(factory));

            _routes[key] = factory;
        }

        public bool CanNavigate(string key) => !string.IsNullOrWhiteSpace(key) && _routes.ContainsKey(key);

        public void Navigate(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Route key cannot be null/empty.", nameof(key));

            if (!_routes.TryGetValue(key, out var factory))
                throw new InvalidOperationException($"No route registered for '{key}'.");

            CurrentView = factory();
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
