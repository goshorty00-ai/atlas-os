using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AtlasAI.Models
{
    public sealed class ChatMessage : INotifyPropertyChanged
    {
        private string _content = "";
        private bool _isUser;
        private DateTime _timestamp;

        public string Content
        {
            get => _content;
            set
            {
                var next = value ?? string.Empty;
                if (string.Equals(_content, next, StringComparison.Ordinal))
                    return;

                _content = next;
                OnPropertyChanged();
            }
        }

        public bool IsUser
        {
            get => _isUser;
            set
            {
                if (_isUser == value)
                    return;

                _isUser = value;
                OnPropertyChanged();
            }
        }

        public DateTime Timestamp
        {
            get => _timestamp;
            set
            {
                if (_timestamp == value)
                    return;

                _timestamp = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

