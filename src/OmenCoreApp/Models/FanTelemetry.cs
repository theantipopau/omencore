using System.ComponentModel;

namespace OmenCore.Models
{
    /// <summary>
    /// Source of the RPM data.
    /// </summary>
    public enum RpmSource
    {
        /// <summary>Direct EC register read (most accurate).</summary>
        EcDirect,
        /// <summary>LibreHardwareMonitor SuperIO.</summary>
        HardwareMonitor,
        /// <summary>MSI Afterburner shared memory.</summary>
        Afterburner,
        /// <summary>WMI BIOS query.</summary>
        WmiBios,
        /// <summary>Estimated from duty cycle (least accurate).</summary>
        Estimated,
        /// <summary>Unknown source.</summary>
        Unknown
    }
    
    public class FanTelemetry : INotifyPropertyChanged
    {
        private int _rpm;
        private int _speedRpm;
        private int _dutyCyclePercent;
        private double _temperature;
        private RpmSource _rpmSource = RpmSource.Unknown;
        private TelemetryDataState _rpmState = TelemetryDataState.Unknown;

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
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayRpmText)));
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
        
        /// <summary>
        /// State of RPM telemetry quality for this fan.
        /// </summary>
        public TelemetryDataState RpmState
        {
            get => _rpmState;
            set
            {
                if (_rpmState != value)
                {
                    _rpmState = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RpmState)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayRpmText)));
                }
            }
        }

        /// <summary>
        /// Human-readable RPM text with unavailable-state handling.
        /// </summary>
        public string DisplayRpmText => RpmState == TelemetryDataState.Unavailable
            ? "RPM unavailable (fan responding)"
            : $"{SpeedRpm} RPM";

        /// <summary>
        /// Source of the RPM reading.
        /// </summary>
        public RpmSource RpmSource
        {
            get => _rpmSource;
            set
            {
                if (_rpmSource != value)
                {
                    _rpmSource = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RpmSource)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RpmSourceDisplay)));
                }
            }
        }
        
        /// <summary>
        /// Human-readable RPM source display string.
        /// </summary>
        public string RpmSourceDisplay => RpmSource switch
        {
            RpmSource.EcDirect => "EC",
            RpmSource.HardwareMonitor => "HWMon",
            RpmSource.Afterburner => "MAB",
            RpmSource.WmiBios => "WMI",
            RpmSource.Estimated => "Est",
            _ => "?"
        };

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
