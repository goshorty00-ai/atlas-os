using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace AtlasAI.Views.ViewModels
{
    public sealed class SecurityGaugeViewModel : INotifyPropertyChanged
    {
        private double _value;
        private string _label = "";
        private string _statusText = "OPTIMAL";
        private Brush _statusBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xEE));
        private Geometry _iconGeometry = Geometry.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public double Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }

        public string ValueText => $"{Value:0.0}%";

        public string Label
        {
            get => _label;
            set => SetProperty(ref _label, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public Brush StatusBrush
        {
            get => _statusBrush;
            set => SetProperty(ref _statusBrush, value);
        }

        public Geometry IconGeometry
        {
            get => _iconGeometry;
            set => SetProperty(ref _iconGeometry, value);
        }

        public void ApplyThresholds(GaugeType type)
        {
            var v = Value;
            var status = "OPTIMAL";
            var color = Color.FromRgb(0x22, 0xD3, 0xEE);

            if (type is GaugeType.Cpu or GaugeType.Gpu)
            {
                if (v > 80) { status = "CRITICAL"; color = Color.FromRgb(0xEF, 0x44, 0x44); }
                else if (v > 60) { status = "WARNING"; color = Color.FromRgb(0xF9, 0x73, 0x16); }
            }
            else if (type == GaugeType.Memory)
            {
                if (v > 80) { status = "CRITICAL"; color = Color.FromRgb(0xEF, 0x44, 0x44); }
                else if (v > 70) { status = "WARNING"; color = Color.FromRgb(0xF9, 0x73, 0x16); }
            }
            else if (type == GaugeType.Disk)
            {
                if (v > 90) { status = "CRITICAL"; color = Color.FromRgb(0xEF, 0x44, 0x44); }
                else if (v > 80) { status = "WARNING"; color = Color.FromRgb(0xF9, 0x73, 0x16); }
            }

            StatusText = status;
            StatusBrush = new SolidColorBrush(color);
            OnPropertyChanged(nameof(ValueText));
        }

        public enum GaugeType
        {
            Cpu,
            Gpu,
            Memory,
            Disk
        }

        private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

