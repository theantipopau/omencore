using System.ComponentModel;

namespace OmenCore.Models
{
    public class ServiceToggle : INotifyPropertyChanged
    {
        private bool _enabledByDefault;

        public string Name { get; set; } = string.Empty;
        public string ServiceName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public bool EnabledByDefault
        {
            get => _enabledByDefault;
            set
            {
                if (_enabledByDefault != value)
                {
                    _enabledByDefault = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EnabledByDefault)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
