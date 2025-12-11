using System;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;

namespace OmenCore.Utils
{
    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<object?, Task> _executeAsync;
        private readonly Func<object?, bool>? _canExecute;
        private bool _isExecuting;

        public AsyncRelayCommand(Func<object?, Task> executeAsync, Func<object?, bool>? canExecute = null)
        {
            _executeAsync = executeAsync;
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke(parameter) ?? true);

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter))
            {
                return;
            }

            try
            {
                _isExecuting = true;
                RaiseCanExecuteChanged();
                await _executeAsync(parameter);
            }
            finally
            {
                _isExecuting = false;
                RaiseCanExecuteChanged();
            }
        }

        public void RaiseCanExecuteChanged()
        {
            if (System.Windows.Application.Current?.Dispatcher.CheckAccess() == true)
            {
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    CanExecuteChanged?.Invoke(this, EventArgs.Empty);
                });
            }
        }
    }
}
