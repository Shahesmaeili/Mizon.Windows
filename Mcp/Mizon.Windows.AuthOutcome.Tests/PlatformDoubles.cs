using System.ComponentModel;
using System.Runtime.CompilerServices;
using Mizon.API;
using Mizon.API.Client.Operations.Clients;
using Mizon.Windows.Services;

// Only dependency boundaries are synthetic. LoginPageViewModel and its outcome
// contract are compiled directly from production; no real request/account/token.
namespace Mizon.API
{
    public sealed record AuthContent(string? AccessToken);
    public sealed record AuthError(int Code);
    public sealed record AuthResponse(bool IsSuccess, AuthContent? Content = null, AuthError? Error = null);
    public sealed class MizonApi
    {
        public Func<CancellationToken, Task<AuthResponse?>> Handler { get; set; } = _ => Task.FromResult<AuthResponse?>(null);
        public int Calls { get; private set; }
        public int LoginCalls { get; private set; }
        public int RegistrationCalls { get; private set; }
        public bool UsedAutomaticTokenUpdate { get; private set; }
        public string? Token { get; private set; }
        public Task<AuthResponse?> SendRequestAsync(LoginOperation operation, CancellationToken token)
        { LoginCalls++; UsedAutomaticTokenUpdate |= operation.UpdateToken; return Send(token); }
        public Task<AuthResponse?> SendRequestAsync(RegisterOperation operation, CancellationToken token)
        { RegistrationCalls++; UsedAutomaticTokenUpdate |= operation.UpdateToken; return Send(token); }
        private Task<AuthResponse?> Send(CancellationToken token) { Calls++; return Handler(token); }
        public void SetToken(string token) => Token = token;
    }
    public sealed class MizonSocket;
}

namespace Mizon.API.Client.Operations.Clients
{
    public sealed record LoginRequest
    {
        public string? Username { get; init; }
        public string? EmailAddress { get; init; }
        public string? Password { get; init; }
    }
    public sealed record RegisterRequest
    {
        public string? FirstName { get; init; }
        public string? LastName { get; init; }
        public string? Username { get; init; }
        public string? EmailAddress { get; init; }
        public string? Password { get; init; }
    }
    public sealed record LoginOperation(LoginRequest Request, bool updateToken = false) { public bool UpdateToken => updateToken; }
    public sealed record RegisterOperation(RegisterRequest Request, bool updateToken = false) { public bool UpdateToken => updateToken; }
}

namespace Mizon.Windows.Services
{
    public interface IAudioPlayerService;
    public sealed class SyntheticAudioPlayer : IAudioPlayerService;
    public sealed class LocalStorageService
    {
        public string? Token { get; private set; }
        public int Writes { get; private set; }
        public void SetJwtToken(string token) { Token = token; Writes++; }
    }
    public sealed class NavigationService
    {
        public int Calls { get; private set; }
        public Type? Destination { get; private set; }
        public Dictionary<string, object>? Parameters { get; private set; }
        public bool ThrowOnNavigate { get; set; }
        public void MainNavigate(Type destination, Dictionary<string, object>? parameters = null)
        { Calls++; if (ThrowOnNavigate) throw new InvalidOperationException("sensitive-synthetic-message"); Destination = destination; Parameters = parameters; }
    }
    public sealed class LocalizationService
    {
        public static LocalizationService Current { get; } = new();
        public string GetString(string key) => key;
    }
    public static class AppLog
    {
        public static readonly List<string> Lines = [];
        public static void Information(string message) => Lines.Add(message);
    }
}

namespace Mizon.Windows.Pages { public sealed class WelcomePage; }

namespace Mizon.Windows.ViewModels
{
    public class MainViewModel(MizonApi api, Lazy<MizonSocket> socket, NavigationService navigation, IAudioPlayerService audio) : INotifyPropertyChanged
    {
        protected MizonApi MizonApi { get; } = api;
        protected Lazy<MizonSocket> Socket { get; } = socket;
        protected IAudioPlayerService Audio { get; } = audio;
        public NavigationService NavigationService { get; } = navigation;
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }
    }
    public sealed class RelayCommand(Func<object?, CancellationToken, Task> execute, Func<object?, CancellationToken, bool> canExecute)
    {
        public Task ExecuteAsync(object? parameter, CancellationToken token) => canExecute(parameter, token) ? execute(parameter, token) : Task.CompletedTask;
        public void RaiseCanExecuteChanged() { }
    }
}
