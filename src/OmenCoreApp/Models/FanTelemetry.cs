using System.ComponentModel;

namespace OmenCore.Models
{
    public class FanTelemetry : INotifyPropertyChanged
    {
        private int _rpm;

        public string Name { get; set; } = string.Empty;

        public int Rpm
        {
            get => _rpm;
            set
            {
                if (_rpm != value)
                {
                    _rpm = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Rpm)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
