using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AtlasAI.Controls
{
    public sealed class AddonBrowserItem : INotifyPropertyChanged
    {
        private string _name = "";
        private string _version = "";
        private string _description = "";
        private string _typeSummary = "";
        private string _resourceSummary = "";
        private string _url = "";
        private string _manifestUrl = "";
        private string _iconUrl = "";
        private string _host = "";
        private string _directoryUrl = "";
        private string _installManifestUrl = "";
        private string _starsText = "";
        private bool _isInstalled;
        private bool _hasError;
        private string _errorText = "";

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string Version
        {
            get => _version;
            set => SetProperty(ref _version, value);
        }

        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        public string TypeSummary
        {
            get => _typeSummary;
            set => SetProperty(ref _typeSummary, value);
        }

        public string ResourceSummary
        {
            get => _resourceSummary;
            set => SetProperty(ref _resourceSummary, value);
        }

        public string Url
        {
            get => _url;
            set => SetProperty(ref _url, value);
        }

        public string ManifestUrl
        {
            get => _manifestUrl;
            set => SetProperty(ref _manifestUrl, value);
        }

        public string IconUrl
        {
            get => _iconUrl;
            set => SetProperty(ref _iconUrl, value);
        }

        public string Host
        {
            get => _host;
            set => SetProperty(ref _host, value);
        }

        public string DirectoryUrl
        {
            get => _directoryUrl;
            set => SetProperty(ref _directoryUrl, value);
        }

        public string InstallManifestUrl
        {
            get => _installManifestUrl;
            set => SetProperty(ref _installManifestUrl, value);
        }

        public string StarsText
        {
            get => _starsText;
            set => SetProperty(ref _starsText, value);
        }

        public bool IsInstalled
        {
            get => _isInstalled;
            set => SetProperty(ref _isInstalled, value);
        }

        public bool HasError
        {
            get => _hasError;
            set => SetProperty(ref _hasError, value);
        }

        public string ErrorText
        {
            get => _errorText;
            set => SetProperty(ref _errorText, value);
        }

        public string Initial => string.IsNullOrWhiteSpace(Name)
            ? "+"
            : Name.Trim()[0].ToString().ToUpperInvariant();

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
                return false;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}