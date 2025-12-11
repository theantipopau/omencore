using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OmenCore.ViewModels
{
    public class ViewModelBase : INotifyPropertyChanged
    {
        private bool _isLoading;
        private string _loadingMessage = "Loading...";

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Gets or sets whether this ViewModel is currently performing a long-running operation.
        /// </summary>
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsNotLoading));
                }
            }
        }

        /// <summary>
        /// Inverse of IsLoading for easier binding.
        /// </summary>
        public bool IsNotLoading => !IsLoading;

        /// <summary>
        /// Gets or sets the message to display during loading operations.
        /// </summary>
        public string LoadingMessage
        {
            get => _loadingMessage;
            set
            {
                if (_loadingMessage != value)
                {
                    _loadingMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Helper to execute an async operation with loading state management.
        /// </summary>
        protected async Task ExecuteWithLoadingAsync(Func<Task> operation, string? loadingMessage = null)
        {
            try
            {
                if (loadingMessage != null)
                {
                    LoadingMessage = loadingMessage;
                }
                IsLoading = true;
                await operation();
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Helper to execute an async operation with loading state management and return a result.
        /// </summary>
        protected async Task<T> ExecuteWithLoadingAsync<T>(Func<Task<T>> operation, string? loadingMessage = null)
        {
            try
            {
                if (loadingMessage != null)
                {
                    LoadingMessage = loadingMessage;
                }
                IsLoading = true;
                return await operation();
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
