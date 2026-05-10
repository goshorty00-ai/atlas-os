using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AtlasAI.Models
{
    public enum ServerStatus
    {
        Unknown,
        Online,
        Offline,
        Error
    }

    public sealed class AddonServerItem : INotifyPropertyChanged
    {
        private string _name = "";
        private string _url = "";
        private bool _enabled;
        private DateTime? _lastCheck;
        private ServerStatus _status = ServerStatus.Unknown;
        private string _errorMessage = "";
        private bool _isManagedByAtlas;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string Url
        {
            get => _url;
            set => SetProperty(ref _url, value);
        }

        public bool Enabled
        {
            get => _enabled;
            set => SetProperty(ref _enabled, value);
        }

        public DateTime? LastCheck
        {
            get => _lastCheck;
            set => SetProperty(ref _lastCheck, value);
        }

        public ServerStatus Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public bool IsManagedByAtlas
        {
            get => _isManagedByAtlas;
            set => SetProperty(ref _isManagedByAtlas, value);
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
