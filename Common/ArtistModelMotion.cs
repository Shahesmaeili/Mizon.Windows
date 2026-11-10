using System;
using System.Numerics;

namespace Mizon.Windows.Common;

/// <summary>Responsive drag tracking and a damped spring back to the resting pose.</summary>
internal sealed class ArtistModelMotion
{
    private const float ReturnDuration = .5f;
    private const float SpringFrequency = 24f;
    private const float SpringDampingRatio = .67f;
    public Quaternion Orientation { get; private set; } = Quaternion.Identity;
    public Vector3 Offset { get; private set; }
    private Quaternion _targetOrientation = Quaternion.Identity;
    private Vector3 _targetOffset;
    private Quaternion _returnStartOrientation;
    private Vector3 _returnStartOffset;
    private float _returnElapsed;
    private bool _returning;

    public bool IsSettled => !_returning
        && MathF.Abs(Quaternion.Dot(Orientation, _targetOrientation)) >= .9999995f
        && Vector3.DistanceSquared(Offset, _targetOffset) <= .0001f;

    public void BeginDrag()
    {
        // A new grab during the rebound starts exactly where the model is rendered.
        _returning = false;
        _returnElapsed = 0;
        _targetOrientation = Orientation;
        _targetOffset = Offset;
    }

    public void SetTarget(Quaternion orientation, Vector3 offset)
    {
        _returning = false;
        orientation = Quaternion.Normalize(orientation);
        // q and -q describe the same pose. Keep the short path at a full-turn boundary.
        _targetOrientation = Quaternion.Dot(Orientation, orientation) < 0
            ? Quaternion.Negate(orientation) : orientation;
        _targetOffset = offset;
    }

    public void ReturnTo(Quaternion orientation)
    {
        SetTarget(orientation, Vector3.Zero);
        if (IsSettled)
        {
            SnapToTarget();
            return;
        }
        _returnStartOrientation = Orientation;
        _returnStartOffset = Offset;
        _returnElapsed = 0;
        _returning = true;
    }

    public void Advance(float elapsedSeconds, bool dragging)
    {
        if (_returning)
        {
            AdvanceReturn(elapsedSeconds);
            return;
        }
        // At 60 Hz, a drag catches ~76% of the remaining distance in one frame.
        // Discrete keyboard motion is softer. Input never restarts a timed animation.
        var halfLife = dragging ? .008f : .032f;
        var amount = 1 - MathF.Exp(-MathF.Log(2) * Math.Clamp(elapsedSeconds, 0, .1f) / halfLife);
        Orientation = Quaternion.Normalize(Quaternion.Slerp(Orientation, _targetOrientation, amount));
        Offset = Vector3.Lerp(Offset, _targetOffset, amount);
        if (IsSettled) SnapToTarget();
    }

    private void AdvanceReturn(float elapsedSeconds)
    {
        _returnElapsed = Math.Min(ReturnDuration, _returnElapsed + Math.Max(0, elapsedSeconds));
        if (_returnElapsed >= ReturnDuration)
        {
            SnapToTarget();
            return;
        }

        // Closed-form underdamped response: stable at any frame rate, with a small
        // (~5.9%) rebound. Do not stop at the first crossing: the spring still has
        // velocity there. The 500ms tail finishes within a subpixel of the target.
        var decay = SpringDampingRatio * SpringFrequency;
        var dampedFrequency = SpringFrequency * MathF.Sqrt(1 - SpringDampingRatio * SpringDampingRatio);
        var phase = dampedFrequency * _returnElapsed;
        var displacement = MathF.Exp(-decay * _returnElapsed)
            * (MathF.Cos(phase) + decay / dampedFrequency * MathF.Sin(phase));
        var progress = 1 - displacement;
        Orientation = Quaternion.Normalize(Quaternion.Slerp(_returnStartOrientation, _targetOrientation, progress));
        Offset = Vector3.Lerp(_returnStartOffset, _targetOffset, progress);
    }

    public void SnapToTarget()
    {
        _returning = false;
        _returnElapsed = 0;
        Orientation = _targetOrientation;
        Offset = _targetOffset;
    }
}
