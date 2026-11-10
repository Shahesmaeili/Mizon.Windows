using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Mizon.Windows;

public class RelayCommand : ICommand, IDisposable
{
    private readonly Func<object?, CancellationToken, Task>? _executeAsync;
    private readonly Action<object?>? _execute;
    private readonly Func<object?, CancellationToken, bool>? _canExecuteAsync;
    private readonly Func<object?, bool>? _canExecute;

    private CancellationTokenSource? _cts;
    private bool _disposed;

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
        if (_disposed) return false;
        if (_executeAsync != null)
            return _canExecuteAsync!(parameter, _cts?.Token ?? CancellationToken.None);
        else
            return _canExecute!(parameter);
    }

    public async void Execute(object? parameter = null)
    {
        if (!CanExecute(parameter)) return;
        if (_executeAsync != null)
        {
            _cts?.Cancel();
            var invocation = new CancellationTokenSource();
            _cts = invocation;

            try
            {
                await _executeAsync(parameter, invocation.Token);
            }
            catch (OperationCanceledException)
            {
                // عملیات لغو شد
            }
            finally
            {
                // The running invocation owns its source. Cancelling a previous
                // invocation must not dispose it while its callbacks still use it.
                if (ReferenceEquals(_cts, invocation)) _cts = null;
                invocation.Dispose();
            }
        }
        else
        {
            _execute?.Invoke(parameter);
        }
    }

    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        CanExecuteChanged = null;
    }
}
