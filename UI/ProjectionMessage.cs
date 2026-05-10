using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AtlasAI.UI
{
    /// <summary>
    /// Represents a holographic projection message in the diegetic UI
    /// </summary>
    public class ProjectionMessage : INotifyPropertyChanged
    {
        private string _id;
        private string _sender;
        private string _content;
        private bool _isUser;
        private DateTime _timestamp;
        private bool _isFadingOut;
        private double _opacity = 1.0;

        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public string Sender
        {
            get => _sender;
            set { _sender = value; OnPropertyChanged(); }
        }

        public string Content
        {
            get => _content;
            set { _content = value; OnPropertyChanged(); }
        }

        public bool IsUser
        {
            get => _isUser;
            set { _isUser = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsAtlas)); }
        }

        public bool IsAtlas => !_isUser;

        public DateTime Timestamp
        {
            get => _timestamp;
            set { _timestamp = value; OnPropertyChanged(); OnPropertyChanged(nameof(TimeString)); }
        }

        public string TimeString => _timestamp.ToString("h:mm tt");

        public bool IsFadingOut
        {
            get => _isFadingOut;
            set { _isFadingOut = value; OnPropertyChanged(); }
        }

        public double Opacity
        {
            get => _opacity;
            set { _opacity = value; OnPropertyChanged(); }
        }

        public ProjectionMessage()
        {
            _id = Guid.NewGuid().ToString();
            _timestamp = DateTime.Now;
            _sender = "Atlas";
            _content = "";
            _isUser = false;
        }

        public ProjectionMessage(string sender, string content, bool isUser)
        {
            _id = Guid.NewGuid().ToString();
            _timestamp = DateTime.Now;
            _sender = sender;
            _content = content;
            _isUser = isUser;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
