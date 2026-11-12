using System;
using System.Threading;
using NativeGamepad = Windows.Gaming.Input.Gamepad;

namespace Mizon.Windows.Services.Gamepad;

internal enum AuthGamepadFeedback { Success, Error, Validation }

internal readonly record struct AuthGamepadFeedbackFrame(double LeftMotor, double RightMotor, bool IsComplete);

// Finite envelopes, sampled by the existing foreground input timer. No separate
// task, timer, buffer allocation or frame-by-frame async work is needed.
internal static class AuthGamepadFeedbackPattern
{
    internal static int Duration(AuthGamepadFeedback kind) => kind switch
    {
        AuthGamepadFeedback.Success => 260,
        AuthGamepadFeedback.Error => 360,
        AuthGamepadFeedback.Validation => 100,
        _ => 0
    };

    internal static AuthGamepadFeedbackFrame Sample(AuthGamepadFeedback kind, long elapsed)
    {
        var duration = Duration(kind);
        if (duration == 0 || elapsed >= duration) return new(0, 0, true);
        if (elapsed <= 0) return default;
        return kind switch
        {
            AuthGamepadFeedback.Success when elapsed < 90 => Pulse(elapsed, 90, .08, .18),
            AuthGamepadFeedback.Success when elapsed >= 140 => Pulse(elapsed - 140, 120, .14, .28),
            AuthGamepadFeedback.Error when elapsed < 140 => Pulse(elapsed, 140, .38, .18),
            AuthGamepadFeedback.Error when elapsed >= 220 => Pulse(elapsed - 220, 140, .38, .18),
            AuthGamepadFeedback.Validation => Pulse(elapsed, 100, 0, .16),
            _ => default
        };
    }

    private static AuthGamepadFeedbackFrame Pulse(long elapsed, int duration, double left, double right)
    {
        const double edge = 25;
        var envelope = Math.Clamp(Math.Min(elapsed / edge, (duration - elapsed) / edge), 0, 1);
        // Smoothstep softens both motor edges without allocating keyframes.
        envelope *= envelope * (3 - 2 * envelope);
        return new(left * envelope, right * envelope, false);
    }
}

// Only a real successful authentication creates this page-navigation payload.
// A normal Welcome startup/retry cannot synthesize or replay success feedback.
// Weak references avoid retaining a window/controller if navigation never consumes it.
internal sealed class AuthGamepadSuccessFeedback : IDisposable
{
    internal const string ParameterKey = "AuthGamepadSuccessFeedback";
    private WeakReference<object>? _owner;
    private WeakReference<NativeGamepad>? _controller;
    private readonly long _createdAt;
    private int _consumed;

    internal AuthGamepadSuccessFeedback(object owner, NativeGamepad controller, long createdAt)
    {
        _owner = new(owner);
        _controller = new(controller);
        _createdAt = createdAt;
    }

    internal bool TryConsume(object owner, NativeGamepad controller, long now)
    {
        if (Interlocked.Exchange(ref _consumed, 1) != 0) return false;
        var sameOwner = _owner?.TryGetTarget(out var originalOwner) == true && ReferenceEquals(owner, originalOwner);
        var sameController = _controller?.TryGetTarget(out var originalController) == true && controller.Equals(originalController);
        _owner = null;
        _controller = null;
        return sameOwner && sameController && now >= _createdAt && now - _createdAt < 2000;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _consumed, 1);
        _owner = null;
        _controller = null;
    }
}
