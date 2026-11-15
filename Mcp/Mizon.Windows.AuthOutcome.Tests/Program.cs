using Mizon.API;
using Mizon.Windows.Pages;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels;

internal static class Program
{
    private static int _checks;
    private const string SyntheticToken = "synthetic-test-token-never-sent";

    private static async Task Main()
    {
        await LoginOutcomes();
        await RegistrationOutcomes();
        await SuccessOrderingAndObserverIsolation();
        await DuplicateAndCancellation();
        await NonSubmissionChanges();
        Check(AppLog.Lines.All(line => !line.Contains(SyntheticToken, StringComparison.Ordinal)
            && !line.Contains("sensitive-synthetic-message", StringComparison.Ordinal)),
            "Production diagnostics omit synthetic tokens and exception messages");
        Console.WriteLine($"PASS {_checks} source-linked authentication outcome assertions. Synthetic API/storage/navigation only; no real account or controller.");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        _checks++;
        Console.WriteLine("PASS " + description);
    }

    private static async Task LoginOutcomes()
    {
        var invalidIdentifier = new Fixture();
        invalidIdentifier.ViewModel.EmailAddressOrUsername = "name\u200f@example.com";
        await invalidIdentifier.Submit();
        Check(invalidIdentifier.Api.LoginCalls == 0
            && invalidIdentifier.Outcomes.SequenceEqual([AuthSubmissionOutcome.Validation])
            && invalidIdentifier.ViewModel.ErrorCode == "AuthInvalidIdentifierError",
            "Injected bidi formatting is rejected before the real login operation");

        var repeated = new Fixture();
        repeated.Api.Handler = _ => Task.FromResult<AuthResponse?>(new(false, Error: new(401)));
        await repeated.Submit();
        await repeated.Submit();
        Check(repeated.Outcomes.SequenceEqual([AuthSubmissionOutcome.Error, AuthSubmissionOutcome.Error])
            && repeated.Api.LoginCalls == 2 && repeated.ViewModel.ErrorCode == "AuthInvalidCredentialsError",
            "Two actual attempts with the same backend error each emit one error outcome");
        repeated.AssertNoSignIn();

        var network = new Fixture();
        network.Api.Handler = _ => throw new IOException("sensitive-synthetic-message");
        await network.Submit();
        Check(network.Outcomes.SequenceEqual([AuthSubmissionOutcome.Error]) && network.ViewModel.ControlsIsEnabled
            && network.ViewModel.HasError, "A network exception emits one error and releases busy state");
        network.AssertNoSignIn();

        foreach (var response in new AuthResponse?[] { null, new(true), new(true, new(null)), new(true, new("  ")) })
        {
            var invalid = new Fixture();
            invalid.Api.Handler = _ => Task.FromResult(response);
            await invalid.Submit();
            Check(invalid.Outcomes.SequenceEqual([AuthSubmissionOutcome.Error]),
                "Missing or empty token never becomes successful feedback");
            invalid.AssertNoSignIn();
        }
    }

    private static async Task RegistrationOutcomes()
    {
        var validation = new Fixture(registration: true);
        validation.ViewModel.RegistrationConfirmPassword = "mismatch";
        await validation.Submit();
        await validation.Submit();
        Check(validation.Outcomes.SequenceEqual([AuthSubmissionOutcome.Validation, AuthSubmissionOutcome.Validation])
            && validation.Api.Calls == 0 && validation.ViewModel.ControlsIsEnabled,
            "Each invalid registration emits one validation outcome without contacting the API");
        validation.AssertNoSignIn();

        var rejected = new Fixture(registration: true);
        rejected.Api.Handler = _ => Task.FromResult<AuthResponse?>(new(false, Error: new(21001)));
        await rejected.Submit();
        Check(rejected.Outcomes.SequenceEqual([AuthSubmissionOutcome.Error]) && rejected.Api.RegistrationCalls == 1
            && rejected.ViewModel.ErrorCode == "AuthUsernameTakenError", "Backend registration rejection emits error rather than local validation");
        rejected.AssertNoSignIn();

        var registered = new Fixture(registration: true);
        await registered.Submit();
        Check(registered.Outcomes.SequenceEqual([AuthSubmissionOutcome.Success]) && registered.Api.RegistrationCalls == 1
            && registered.Navigation.Destination == typeof(WelcomePage) && registered.Storage.Token == SyntheticToken
            && registered.ViewModel.RegistrationPassword.Length == 0 && registered.ViewModel.RegistrationConfirmPassword.Length == 0,
            "Successful registration commits one outcome, clears sensitive fields and opens Welcome");
    }

    private static async Task SuccessOrderingAndObserverIsolation()
    {
        var fixture = new Fixture();
        var ticket = new object();
        var navigation = new Dictionary<string, object> { ["synthetic-success-ticket"] = ticket };
        var observedAfterCommitBeforeNavigation = false;
        fixture.ViewModel.SubmissionCompleted += (_, _) => throw new InvalidOperationException("sensitive-synthetic-message");
        fixture.ViewModel.SubmissionCompleted += (_, args) =>
        {
            observedAfterCommitBeforeNavigation = fixture.Storage.Token == SyntheticToken && fixture.Api.Token == SyntheticToken
                && fixture.ViewModel.Password.Length > 0 && fixture.Navigation.Calls == 0;
            args.NavigationParameters = navigation;
        };
        await fixture.Submit();
        Check(fixture.Outcomes.SequenceEqual([AuthSubmissionOutcome.Success]) && observedAfterCommitBeforeNavigation
            && fixture.Storage.Writes == 1 && fixture.Navigation.Calls == 1 && !fixture.Api.UsedAutomaticTokenUpdate,
            "Success follows explicit token commit and precedes navigation; throwing feedback observer cannot break sign-in");
        Check(ReferenceEquals(fixture.Navigation.Parameters, navigation) && ReferenceEquals(fixture.Navigation.Parameters!["synthetic-success-ticket"], ticket)
            && fixture.ViewModel.Password.Length == 0, "Navigation preserves the one-shot feedback ticket and clears the password first");
        Check(!fixture.ViewModel.HasError && !fixture.ViewModel.ShowProgressBar,
            "Observer failure does not become an authentication error or retain loading state");

        var noObserver = new Fixture();
        noObserver.DetachObserver();
        await noObserver.Submit();
        Check(noObserver.Navigation.Calls == 1 && noObserver.Navigation.Parameters is null && noObserver.Storage.Writes == 1,
            "Authentication without a feedback observer still signs in normally");

        var navigationFailure = new Fixture();
        navigationFailure.Navigation.ThrowOnNavigate = true;
        await navigationFailure.Submit();
        Check(navigationFailure.Outcomes.SequenceEqual([AuthSubmissionOutcome.Success])
            && navigationFailure.Storage.Writes == 1 && navigationFailure.ViewModel.ControlsIsEnabled,
            "A post-authentication navigation failure cannot publish a second contradictory outcome");
    }

    private static async Task DuplicateAndCancellation()
    {
        var fixture = new Fixture();
        var response = new TaskCompletionSource<AuthResponse?>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Api.Handler = _ => response.Task;
        var first = fixture.Submit();
        Check(fixture.ViewModel.ShowProgressBar && fixture.Api.Calls == 1, "Pending request marks the form busy synchronously");
        await fixture.Submit();
        Check(fixture.Api.Calls == 1 && fixture.Outcomes.Count == 0, "Repeated submit while busy neither requests nor emits feedback");
        response.SetResult(new(false, Error: new(401)));
        await first;
        Check(fixture.Outcomes.SequenceEqual([AuthSubmissionOutcome.Error]) && fixture.ViewModel.ControlsIsEnabled,
            "The original pending request alone completes and releases busy ownership");

        using var preCancelled = new CancellationTokenSource();
        preCancelled.Cancel();
        var before = new Fixture();
        await before.ViewModel.SubmitAsync(preCancelled.Token);
        Check(before.Api.Calls == 0 && before.Outcomes.Count == 0, "Already-cancelled submission stays silent and never calls the API");
        before.AssertNoSignIn();

        using var inFlight = new CancellationTokenSource();
        var cancelled = new Fixture();
        cancelled.Api.Handler = async token => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return null; };
        var active = cancelled.ViewModel.SubmitAsync(inFlight.Token);
        inFlight.Cancel();
        await active;
        Check(cancelled.Outcomes.Count == 0 && !cancelled.ViewModel.HasError && cancelled.ViewModel.Password.Length == 0
            && cancelled.ViewModel.ControlsIsEnabled, "In-flight cancellation clears credentials and emits no error or haptic outcome");
        cancelled.AssertNoSignIn();

        foreach (var lateFailure in new[] { false, true })
        {
            using var lifetime = new CancellationTokenSource();
            var late = new Fixture();
            var completion = new TaskCompletionSource<AuthResponse?>(TaskCreationOptions.RunContinuationsAsynchronously);
            late.Api.Handler = _ => completion.Task; // Deliberately ignores page cancellation.
            var pending = late.ViewModel.SubmitAsync(lifetime.Token);
            lifetime.Cancel();
            if (lateFailure) completion.SetException(new IOException("sensitive-synthetic-message"));
            else completion.SetResult(new(true, new(SyntheticToken)));
            await pending;
            Check(late.Outcomes.Count == 0 && !late.ViewModel.HasError && late.ViewModel.ControlsIsEnabled,
                "A late response or exception from an abandoned page is silent");
            late.AssertNoSignIn();
        }
    }

    private static async Task NonSubmissionChanges()
    {
        var fixture = new Fixture();
        fixture.ViewModel.Password = string.Empty;
        await fixture.Submit();
        fixture.ViewModel.RefreshLocalization();
        fixture.ViewModel.EmailAddressOrUsername = "changed.synthetic";
        fixture.ViewModel.SetRegistrationMode(true);
        fixture.ViewModel.SetRegistrationMode(false);
        Check(fixture.Outcomes.Count == 0 && fixture.Api.Calls == 0,
            "Disabled submit, localization, credential edits and mode switches never fabricate submission feedback");
        var rejected = new Fixture();
        rejected.Api.Handler = _ => Task.FromResult<AuthResponse?>(new(false, Error: new(401)));
        await rejected.Submit();
        rejected.ViewModel.RefreshLocalization();
        rejected.ViewModel.EmailAddressOrUsername += ".changed";
        rejected.ViewModel.ClearSensitiveInputs();
        Check(rejected.Outcomes.SequenceEqual([AuthSubmissionOutcome.Error]) && !rejected.ViewModel.HasError,
            "Translating or clearing a displayed error does not replay its haptic outcome");
    }

    private sealed class Fixture
    {
        public MizonApi Api { get; } = new();
        public NavigationService Navigation { get; } = new();
        public LocalStorageService Storage { get; } = new();
        public LoginPageViewModel ViewModel { get; }
        public List<AuthSubmissionOutcome> Outcomes { get; } = [];
        public Fixture(bool registration = false)
        {
            Api.Handler = _ => Task.FromResult<AuthResponse?>(new(true, new(SyntheticToken)));
            ViewModel = new(Api, new(() => throw new InvalidOperationException("Authentication must not initialize a socket.")),
                Navigation, Storage, new SyntheticAudioPlayer());
            ViewModel.SetRegistrationMode(registration);
            if (registration)
            {
                ViewModel.RegistrationFirstName = "Synthetic";
                ViewModel.RegistrationLastName = "Tester";
                ViewModel.RegistrationUsername = "synthetic.tester";
                ViewModel.RegistrationEmailAddress = "synthetic@example.invalid";
                ViewModel.RegistrationPassword = "synthetic-password";
                ViewModel.RegistrationConfirmPassword = "synthetic-password";
            }
            else
            {
                ViewModel.EmailAddressOrUsername = "synthetic.tester";
                ViewModel.Password = "synthetic-password";
            }
            ViewModel.SubmissionCompleted += OnOutcome;
        }
        private void OnOutcome(object? sender, AuthSubmissionCompletedEventArgs args) => Outcomes.Add(args.Outcome);
        public void DetachObserver() => ViewModel.SubmissionCompleted -= OnOutcome;
        public Task Submit() => ViewModel.SubmitAsync(CancellationToken.None);
        public void AssertNoSignIn() => Check(Storage.Writes == 0 && Api.Token is null && Navigation.Calls == 0,
            "Non-success leaves token storage and navigation untouched");
    }
}
