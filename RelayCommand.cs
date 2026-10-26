using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Mizon.Windows;

public class RelayCommand : ICommand
{
    private readonly Func<object?, CancellationToken, Task>? _executeAsync;
    private readonly Action<object?>? _execute;
    private readonly Func<object?, CancellationToken, bool>? _canExecuteAsync;
    private readonly Func<object?, bool>? _canExecute;

    private CancellationTokenSource _cts = new();

    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// Async constructor
    /// </summary>
    public RelayCommand(
        Func<object?, CancellationToken, Task> executeAsync,
        Func<object?, CancellationToken, bool>? canExecuteAsync = null)
    {
        _executeAsync = executeAsync;
        _canExecuteAsync = canExecuteAsync ?? ((_, __) => true);
    }

    /// <summary>
    /// Sync constructor
    /// </summary>
    public RelayCommand(
        Action<object?> execute,
        Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute ?? (_ => true);
    }

    public bool CanExecute(object? parameter)
    {
        if (_executeAsync != null)
            return _canExecuteAsync!(parameter, _cts.Token);
        else
            return _canExecute!(parameter);
    }

    public async void Execute(object? parameter = null)
    {
        if (_executeAsync != null)
        {
            _cts.Cancel(); // لغو عملیات قبلی
            _cts = new CancellationTokenSource();

            try
            {
                await _executeAsync(parameter, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                // عملیات لغو شد
            }
        }
        else
        {
            _execute?.Invoke(parameter);
        }
    }

    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
