using System;
using System.Windows.Input;

namespace OmenCore.Utils
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

        public void Execute(object? parameter) => _execute(parameter);

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
