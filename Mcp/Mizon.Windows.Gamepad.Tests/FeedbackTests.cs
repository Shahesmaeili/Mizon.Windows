using Microsoft.UI.Xaml;
using Mizon.Windows.Services.Gamepad;
using Windows.Gaming.Input;

internal static class FeedbackTests
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (var kind in Enum.GetValues<AuthGamepadFeedback>())
        {
            var duration = AuthGamepadFeedbackPattern.Duration(kind);
            check(duration is > 0 and <= 400, $"{kind} has a short bounded duration");
            var bounded = true;
            var audible = false;
            for (var elapsed = -5; elapsed <= duration + 1000; elapsed++)
            {
                var frame = AuthGamepadFeedbackPattern.Sample(kind, elapsed);
                bounded &= frame.LeftMotor is >= 0 and <= .4 && frame.RightMotor is >= 0 and <= .4;
                audible |= frame.LeftMotor > 0 || frame.RightMotor > 0;
                if (elapsed >= duration) bounded &= frame.IsComplete && frame.LeftMotor == 0 && frame.RightMotor == 0;
            }
            check(bounded && audible, $"{kind} never exceeds bounded motor power and finishes at zero");
            var edge = AuthGamepadFeedbackPattern.Sample(kind, 1);
            var center = AuthGamepadFeedbackPattern.Sample(kind, 30);
            check(edge.LeftMotor < center.LeftMotor || edge.RightMotor < center.RightMotor, $"{kind} rises through a soft edge");
        }
        check(AuthGamepadFeedbackPattern.Sample(AuthGamepadFeedback.Success, 115) == default, "Success has a real silent gap between pulses");
        check(AuthGamepadFeedbackPattern.Sample(AuthGamepadFeedback.Error, 180) == default, "Error has a real silent gap between pulses");
        check(AuthGamepadFeedbackPattern.Sample((AuthGamepadFeedback)99, 20).IsComplete, "Undefined feedback kind has no output");

        Gamepad.Reset();
        var controller = new Gamepad();
        Gamepad.Attach(controller);
        var root = new FrameworkElement();
        root.Load();
        long now = 1000;
        using (var session = new AuthGamepadSession(root, _ => { }, () => now))
        {
            session.SetActive(true);
            var timer = root.DispatcherQueue.Timers.Single();
            timer.Fire();
            check(!session.PlayFeedback(AuthGamepadFeedback.Error) && session.LastFeedback is null, "Disabled feedback cannot start or report an outcome");
            check(session.CreateSuccessFeedback(new object()) is null, "Disabled source cannot create a success handoff");
            session.FeedbackEnabled = true;
            foreach (var kind in Enum.GetValues<AuthGamepadFeedback>())
            {
                check(session.PlayFeedback(kind) && session.IsFeedbackPlaying && session.LastFeedback == kind, $"{kind} starts through the real session");
                now += 30;
                timer.Fire();
                var expected = AuthGamepadFeedbackPattern.Sample(kind, 30);
                check(controller.Vibration.LeftMotor == expected.LeftMotor && controller.Vibration.RightMotor == expected.RightMotor,
                    $"{kind} sends the actual envelope to both native motor fields");
                controller.Reading = new() { Buttons = GamepadButtons.A };
                now += 1;
                timer.Fire();
                expected = AuthGamepadFeedbackPattern.Sample(kind, 31);
                check(controller.Vibration.LeftMotor == expected.LeftMotor && controller.Vibration.RightMotor == expected.RightMotor,
                    "Navigation acceptance cannot overwrite outcome feedback");
                controller.Reading = default;
                now += 1000;
                timer.Fire();
                check(!session.IsFeedbackPlaying && Silent(controller), $"{kind} automatically stops both motors");
            }
            check(!session.PlayFeedback((AuthGamepadFeedback)99), "Session rejects undefined patterns");
            session.PlayFeedback(AuthGamepadFeedback.Error);
            now += 30; timer.Fire();
            session.PlayFeedback(AuthGamepadFeedback.Validation);
            now += 30; timer.Fire();
            check(session.LastFeedback == AuthGamepadFeedback.Validation && controller.Vibration.LeftMotor == 0,
                "A new outcome replaces, rather than queues or overlaps, the previous pattern");
            session.FeedbackEnabled = false;
            check(!session.IsFeedbackPlaying && Silent(controller), "Opt-out immediately stops all outcome motors");
            now += 1000; timer.Fire();
            check(Silent(controller), "Old timer ticks cannot restart opted-out feedback");
            session.FeedbackEnabled = true;
            session.PlayFeedback(AuthGamepadFeedback.Error);
            now += 30; timer.Fire();
            session.SetActive(false);
            check(!session.IsFeedbackPlaying && Silent(controller) && !timer.IsRunning, "Deactivation cancels outcome and polling");
            check(!session.PlayFeedback(AuthGamepadFeedback.Success), "Inactive page refuses outcome feedback");
            session.SetActive(true);
            session.PlayFeedback(AuthGamepadFeedback.Error);
            now += 30; timer.Fire();
            root.Pointer();
            check(!session.IsFeedbackPlaying && Silent(controller), "Competing pointer cancels result even outside gamepad mode");
            session.PlayFeedback(AuthGamepadFeedback.Error);
            now += 30; timer.Fire();
            root.Unload();
            check(!session.IsFeedbackPlaying && Silent(controller), "Unloading cancels outcome");
            root.Load();
            session.PlayFeedback(AuthGamepadFeedback.Success);
            now += 30; timer.Fire();
            Gamepad.Detach(controller);
            root.DispatcherQueue.Drain();
            check(!session.IsFeedbackPlaying && Silent(controller), "Disconnect cancels outcome on its originating device");
            check(!session.PlayFeedback(AuthGamepadFeedback.Success), "No controller cannot report an accepted outcome");
            Gamepad.Attach(controller);
            root.DispatcherQueue.Drain();
            controller.FailVibration = true;
            var previous = session.LastFeedback;
            check(!session.PlayFeedback(AuthGamepadFeedback.Validation) && session.LastFeedback == previous && !session.IsFeedbackPlaying,
                "Native motor failure is sanitized and cannot report a new accepted outcome");
            controller.FailVibration = false;
            session.PlayFeedback(AuthGamepadFeedback.Error);
            now += 30; timer.Fire();
            session.Dispose();
            check(Silent(controller) && !session.IsFeedbackPlaying && !timer.IsRunning && timer.SubscriberCount == 0,
                "Disposal cancels all result output and detaches the timer");
            check(!session.PlayFeedback(AuthGamepadFeedback.Success), "Disposed source never starts feedback");
        }

        Gamepad.Reset();
        controller = new Gamepad();
        Gamepad.Attach(controller);
        root = new FrameworkElement(); root.Load();
        var owner = new object();
        using var source = new AuthGamepadSession(root, _ => { }, () => now);
        source.SetActive(true); source.FeedbackEnabled = true;
        AuthGamepadSuccessFeedback Ticket() => source.CreateSuccessFeedback(owner)!;
        var destinationRoot = new FrameworkElement(); destinationRoot.Load();
        using (var destination = new AuthGamepadSession(destinationRoot, _ => { }, () => now))
        {
            destination.SetActive(true);
            var ticket = Ticket();
            check(ticket is not null && !source.IsFeedbackPlaying, "Success ticket does not start a motor on the departing page");
            check(destination.ConsumeSuccessFeedback(ticket!, owner) && destination.FeedbackEnabled && destination.IsFeedbackPlaying,
                "Immediate same-window same-controller Welcome consumes enabled success intent");
            check(!destination.ConsumeSuccessFeedback(ticket!, owner), "A success ticket cannot replay");
            var timer = destinationRoot.DispatcherQueue.Timers.Single();
            now += 30; timer.Fire();
            check(!Silent(controller), "Consumed success runs on the destination's lifetime-owned timer");
            destination.SetActive(false);
            check(Silent(controller), "Destination deactivation stops transferred success");
            destination.SetActive(true);
            var wrongOwner = Ticket();
            check(!destination.ConsumeSuccessFeedback(wrongOwner, new object()) && !destination.IsFeedbackPlaying, "A different window cannot consume success");
            check(!destination.ConsumeSuccessFeedback(wrongOwner, owner), "Rejected ticket is still single-use");
            var expired = Ticket();
            now += 2000;
            check(!destination.ConsumeSuccessFeedback(expired, owner), "Old success cannot replay on a later Welcome load");
            var clockBackwards = Ticket();
            now--;
            check(!destination.ConsumeSuccessFeedback(clockBackwards, owner), "Invalid backward-clock ticket is refused");
            now++;
            var disposed = Ticket(); disposed.Dispose();
            check(!destination.ConsumeSuccessFeedback(disposed, owner), "Abandoned success payload can be explicitly disposed");
            var refused = Ticket();
            destination.FeedbackEnabled = false;
            check(!destination.ConsumeSuccessFeedback(refused, owner) && !destination.FeedbackEnabled,
                "Handoff cannot override an explicit later destination opt-out");
        }
        var disabledRoot = new FrameworkElement(); disabledRoot.Load();
        using (var inactive = new AuthGamepadSession(disabledRoot, _ => { }, () => now))
        {
            check(!inactive.ConsumeSuccessFeedback(Ticket(), owner), "Background Welcome cannot play deferred success");
        }
        var replacement = new Gamepad();
        var changedDeviceTicket = Ticket();
        var completedNavigationTicket = Ticket();
        source.Dispose();
        var arrivedRoot = new FrameworkElement(); arrivedRoot.Load();
        using (var arrived = new AuthGamepadSession(arrivedRoot, _ => { }, () => now))
        {
            arrived.SetActive(true);
            check(arrived.ConsumeSuccessFeedback(completedNavigationTicket, owner),
                "Source disposal during successful navigation does not discard the one-shot destination intent");
            now += 30; arrivedRoot.DispatcherQueue.Timers.Single().Fire();
            check(!Silent(controller), "Destination continues success after the source session has been disposed");
        }
        check(Silent(controller), "Disposing the destination ends transferred success without a global motor owner");
        Gamepad.Detach(controller); Gamepad.Attach(replacement);
        var changedRoot = new FrameworkElement(); changedRoot.Load();
        using (var changed = new AuthGamepadSession(changedRoot, _ => { }, () => now))
        {
            changed.SetActive(true);
            check(!changed.ConsumeSuccessFeedback(changedDeviceTicket, owner) && !changed.FeedbackEnabled && Silent(replacement),
                "Success never transfers to a different controller");
        }
        root.DispatcherQueue.Drain();
        source.Dispose();
        Gamepad.Reset();
    }

    private static bool Silent(Gamepad controller) => controller.Vibration.LeftMotor == 0 && controller.Vibration.RightMotor == 0;
}
