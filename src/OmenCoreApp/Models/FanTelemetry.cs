using System.ComponentModel;

namespace OmenCore.Models
{
    public class FanTelemetry : INotifyPropertyChanged
    {
        private int _rpm;
        private int _speedRpm;
        private int _dutyCyclePercent;
        private double _temperature;

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

        public int SpeedRpm
        {
            get => _speedRpm;
            set
            {
                if (_speedRpm != value)
                {
                    _speedRpm = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SpeedRpm)));
                    // Also update Rpm for backward compatibility
                    Rpm = value;
                }
            }
        }

        public int DutyCyclePercent
        {
            get => _dutyCyclePercent;
            set
            {
                if (_dutyCyclePercent != value)
                {
                    _dutyCyclePercent = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DutyCyclePercent)));
                }
            }
        }

        public double Temperature
        {
            get => _temperature;
            set
            {
                if (Math.Abs(_temperature - value) > 0.1)
                {
                    _temperature = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Temperature)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
