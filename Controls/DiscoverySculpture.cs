using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Windows.UI;

namespace Mizon.Windows.Controls;

public enum DiscoverySculptureKind { BeadedOrb, Crystal }

/// <summary>A small perspective-rendered sculpture. Geometry and depth, rather than the surrounding card, rotate.</summary>
public sealed class DiscoverySculpture : UserControl
{
    private const float DefaultPitch = -0.24f;
    private const float DefaultYaw = 0.38f;
    private readonly DispatcherQueueTimer _timer;
    private readonly global::Windows.UI.ViewManagement.UISettings _settings = new();
    private readonly List<Bead> _beads = new(280);
    private readonly List<Face> _faces = new(400);
    private readonly DrawItem[] _drawItems = new DrawItem[800];
    private readonly CanvasRadialGradientBrush?[] _beadBrushes = new CanvasRadialGradientBrush?[12];
    private CanvasControl? _canvas;
    private CanvasGeometry? _triangle;
    private float _pitch = DefaultPitch, _yaw = DefaultYaw;
    private float _targetPitch = DefaultPitch, _targetYaw = DefaultYaw;
    private float _restPitch = DefaultPitch, _restYaw = DefaultYaw;
    private Vector2 _lastPointer;
    private bool _dragging;
    private uint _capturedPointer;
    private long _lastTick;

    public DiscoverySculpture()
    {
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        MinWidth = 120;
        MinHeight = 120;
        AutomationProperties.SetName(this, "مجسمهٔ سه‌بعدی موسیقی");
        AutomationProperties.SetHelpText(this, "برای چرخاندن، با موس بکشید یا از کلیدهای جهت استفاده کنید. Home زاویه را بازنشانی می‌کند.");
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.Tick += Tick;
        Loaded += ControlLoaded;
        Unloaded += ControlUnloaded;
        PointerMoved += PointerMovedOnSculpture;
        PointerPressed += PointerPressedOnSculpture;
        PointerReleased += PointerReleasedOnSculpture;
        PointerCanceled += PointerReleasedOnSculpture;
        PointerCaptureLost += PointerCaptureLostOnSculpture;
        PointerExited += PointerExitedOnSculpture;
        KeyDown += KeyDownOnSculpture;
        BuildScene();
    }

    public DiscoverySculptureKind SculptureKind
    {
        get => (DiscoverySculptureKind)GetValue(SculptureKindProperty);
        set => SetValue(SculptureKindProperty, value);
    }
    public static readonly DependencyProperty SculptureKindProperty = DependencyProperty.Register(
        nameof(SculptureKind), typeof(DiscoverySculptureKind), typeof(DiscoverySculpture),
        new PropertyMetadata(DiscoverySculptureKind.BeadedOrb, OnKindChanged));

    public Color AccentColor
    {
        get => (Color)GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }
    public static readonly DependencyProperty AccentColorProperty = DependencyProperty.Register(
        nameof(AccentColor), typeof(Color), typeof(DiscoverySculpture),
        new PropertyMetadata(Color.FromArgb(255, 30, 219, 239), OnAccentChanged));

    private static void OnKindChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (DiscoverySculpture)sender;
        control.BuildScene();
        control._canvas?.Invalidate();
    }

    private static void OnAccentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (DiscoverySculpture)sender;
        control.DisposeBrushes();
        control._canvas?.Invalidate();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);

    private void ControlLoaded(object sender, RoutedEventArgs args)
    {
        if (_canvas is not null) return;
        _canvas = new CanvasControl { ClearColor = Color.FromArgb(0, 0, 0, 0), IsHitTestVisible = false };
        _canvas.CreateResources += CreateResources;
        _canvas.Draw += Draw;
        Content = _canvas;
    }

    private void ControlUnloaded(object sender, RoutedEventArgs args)
    {
        _timer.Stop();
        _dragging = false;
        ReleasePointerCaptures();
        if (_canvas is { } canvas)
        {
            canvas.Draw -= Draw;
            canvas.CreateResources -= CreateResources;
            canvas.RemoveFromVisualTree();
            Content = null;
            _canvas = null;
        }
        DisposeResources();
    }

    private void CreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
    {
        // Recreate device-bound resources after a device reset as well as after unloading.
        DisposeResources();
        EnsureResources(sender);
    }

    private void EnsureResources(CanvasControl sender)
    {
        _triangle ??= CanvasGeometry.CreatePolygon(sender, [Vector2.Zero, Vector2.UnitX, Vector2.UnitY]);
        if (_beadBrushes[0] is not null) return;
        for (var index = 0; index < _beadBrushes.Length; index++)
        {
            var amount = index / (float)(_beadBrushes.Length - 1);
            var color = Mix(Color.FromArgb(255, 15, 46, 104), AccentColor, amount);
            _beadBrushes[index] = new CanvasRadialGradientBrush(sender,
                Mix(color, Color.FromArgb(255, 223, 249, 255), 0.42f), Shade(color, 0.28f));
        }
    }

    private void DisposeBrushes()
    {
        for (var index = 0; index < _beadBrushes.Length; index++)
        {
            _beadBrushes[index]?.Dispose();
            _beadBrushes[index] = null;
        }
    }

    private void DisposeResources()
    {
        DisposeBrushes();
        _triangle?.Dispose();
        _triangle = null;
    }

    private void BuildScene()
    {
        _beads.Clear();
        _faces.Clear();
        if (SculptureKind == DiscoverySculptureKind.Crystal)
        {
            BuildCrystal();
            return;
        }
        const int count = 270;
        const float goldenAngle = 2.39996323f;
        for (var index = 0; index < count; index++)
        {
            var y = 1f - 2f * (index + 0.5f) / count;
            var angle = index * goldenAngle;
            var radius = MathF.Sqrt(1f - y * y);
            var lobe = 0.91f + 0.075f * MathF.Sin(angle * 3f + y * 5f);
            var position = new Vector3(MathF.Cos(angle) * radius, y, MathF.Sin(angle) * radius) * lobe;
            var palette = Math.Clamp((int)((0.5f + 0.5f * MathF.Sin(angle + y * 3.3f)) * 11f), 0, 11);
            _beads.Add(new Bead(position, 0.088f + 0.018f * (0.5f + 0.5f * MathF.Sin(index * 4.7f)), palette));
        }
        AddRibbon(Matrix4x4.CreateRotationX(-0.58f) * Matrix4x4.CreateRotationZ(-0.49f), 1.11f, 0.19f, 0);
        AddRibbon(Matrix4x4.CreateRotationX(0.94f) * Matrix4x4.CreateRotationZ(0.70f), 1.15f, 0.10f, 1);
    }

    private void AddRibbon(Matrix4x4 rotation, float radius, float width, int palette)
    {
        const int segments = 80;
        Vector3 Point(float angle, float side)
        {
            var wave = 0.07f * MathF.Sin(angle * 3f);
            return Vector3.Transform(new Vector3(MathF.Cos(angle) * (radius + side * width), wave + side * 0.08f,
                MathF.Sin(angle) * (radius + side * width)), rotation);
        }
        for (var index = 0; index < segments; index++)
        {
            var angle = index * MathF.Tau / segments;
            var next = (index + 1) * MathF.Tau / segments;
            var a = Point(angle, -1f); var b = Point(angle, 1f);
            var c = Point(next, 1f); var d = Point(next, -1f);
            _faces.Add(new Face(a, b, c, palette, true));
            _faces.Add(new Face(a, c, d, palette, true));
        }
    }

    private void BuildCrystal()
    {
        const int sides = 8;
        var crown = new Vector3[sides];
        var girdle = new Vector3[sides];
        var lower = new Vector3[sides];
        for (var index = 0; index < sides; index++)
        {
            var angle = MathF.Tau * index / sides;
            crown[index] = new Vector3(MathF.Cos(angle) * 0.44f, -0.67f, MathF.Sin(angle) * 0.44f);
            girdle[index] = new Vector3(MathF.Cos(angle) * 0.88f, -0.12f, MathF.Sin(angle) * 0.88f);
            lower[index] = new Vector3(MathF.Cos(angle) * 0.78f, 0.20f, MathF.Sin(angle) * 0.78f);
        }
        for (var index = 0; index < sides; index++)
        {
            var next = (index + 1) % sides;
            _faces.Add(new Face(new Vector3(0, -0.69f, 0), crown[index], crown[next], index, false));
            _faces.Add(new Face(crown[index], girdle[index], girdle[next], index, false));
            _faces.Add(new Face(crown[index], girdle[next], crown[next], index + 2, false));
            _faces.Add(new Face(girdle[index], lower[index], lower[next], index + 1, false));
            _faces.Add(new Face(girdle[index], lower[next], girdle[next], index + 3, false));
            _faces.Add(new Face(lower[index], new Vector3(0, 1.1f, 0), lower[next], index, false));
        }
    }

    private void Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var width = (float)sender.ActualWidth;
        var height = (float)sender.ActualHeight;
        if (width < 1 || height < 1) return;
        EnsureResources(sender);
        var session = args.DrawingSession;
        var center = new Vector2(width * 0.5f, height * 0.46f);
        var scale = MathF.Min(width, height) * 0.29f;
        var rotation = Matrix4x4.CreateRotationX(_pitch) * Matrix4x4.CreateRotationY(_yaw) * Matrix4x4.CreateRotationZ(-0.15f);
        var light = Vector3.Normalize(new Vector3(-0.5f, -0.8f, 1.2f));
        for (var shadow = 9; shadow >= 0; shadow--)
            session.FillEllipse(new Vector2(center.X, center.Y + scale * 1.27f), scale * (0.48f + shadow * 0.045f),
                scale * (0.035f + shadow * 0.012f), Color.FromArgb(5, 4, 10, 24));

        var count = 0;
        foreach (var bead in _beads)
        {
            var position = Vector3.Transform(bead.Position, rotation);
            _drawItems[count++] = new DrawItem(position.Z, Project(position, center, scale), default, default,
                bead.Radius * scale * Perspective(position.Z), bead.Palette, default, true);
        }
        foreach (var face in _faces)
        {
            var a = Vector3.Transform(face.A, rotation);
            var b = Vector3.Transform(face.B, rotation);
            var c = Vector3.Transform(face.C, rotation);
            var normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            var intensity = Math.Clamp(0.48f + MathF.Abs(Vector3.Dot(normal, light)) * 0.5f, 0.35f, 1f);
            var baseColor = face.Ribbon
                ? Mix(Color.FromArgb(230, 80, 122, 208), AccentColor, face.Palette == 0 ? 0.16f : 0.46f)
                : Mix(Shade(AccentColor, 0.6f), Color.FromArgb(255, 222, 216, 255), (face.Palette % 5) * 0.13f);
            var color = Shade(baseColor, intensity);
            if (face.Ribbon) color.A = 215;
            _drawItems[count++] = new DrawItem((a.Z + b.Z + c.Z) / 3f, Project(a, center, scale), Project(b, center, scale),
                Project(c, center, scale), 0, face.Ribbon ? 1 : 0, color, false);
        }
        Array.Sort(_drawItems, 0, count, DrawItemComparer.Instance);
        for (var index = 0; index < count; index++)
        {
            ref var item = ref _drawItems[index];
            if (item.IsBead)
            {
                var brush = _beadBrushes[item.Palette]!;
                brush.Center = item.A;
                brush.OriginOffset = new Vector2(-item.Radius * 0.30f, -item.Radius * 0.35f);
                brush.RadiusX = item.Radius * 1.15f;
                brush.RadiusY = item.Radius * 1.15f;
                session.FillCircle(item.A, item.Radius, brush);
                session.FillCircle(item.A + new Vector2(-0.28f, -0.33f) * item.Radius, item.Radius * 0.12f,
                    Color.FromArgb(65, 233, 255, 255));
            }
            else
            {
                // A single reusable unit triangle avoids allocating a native path for every facet on every frame.
                var ab = item.B - item.A; var ac = item.C - item.A;
                session.Transform = new Matrix3x2(ab.X, ab.Y, ac.X, ac.Y, item.A.X, item.A.Y);
                session.FillGeometry(_triangle!, item.Color);
                session.Transform = Matrix3x2.Identity;
                if (item.Palette == 0)
                {
                    session.DrawLine(item.A, item.B, Color.FromArgb(35, 255, 255, 255), 0.65f);
                    session.DrawLine(item.B, item.C, Color.FromArgb(25, 255, 255, 255), 0.65f);
                }
            }
        }
    }

    private static float Perspective(float depth) => 4.5f / (4.5f - depth);
    private static Vector2 Project(Vector3 point, Vector2 center, float scale) => center + new Vector2(point.X, point.Y) * scale * Perspective(point.Z);
    private static Color Mix(Color from, Color to, float amount) => Color.FromArgb(255,
        (byte)(from.R + (to.R - from.R) * amount), (byte)(from.G + (to.G - from.G) * amount), (byte)(from.B + (to.B - from.B) * amount));
    private static Color Shade(Color color, float value) => Color.FromArgb(color.A,
        (byte)Math.Clamp(color.R * value, 0, 255), (byte)Math.Clamp(color.G * value, 0, 255), (byte)Math.Clamp(color.B * value, 0, 255));

    private void PointerPressedOnSculpture(object sender, PointerRoutedEventArgs args)
    {
        if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus(FocusState.Pointer);
        if (!CapturePointer(args.Pointer)) return;
        _capturedPointer = args.Pointer.PointerId;
        _dragging = true;
        var point = args.GetCurrentPoint(this).Position;
        _lastPointer = new Vector2((float)point.X, (float)point.Y);
        args.Handled = true;
    }

    private void PointerMovedOnSculpture(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(this).Position;
        var position = new Vector2((float)point.X, (float)point.Y);
        if (_dragging && args.Pointer.PointerId == _capturedPointer)
        {
            var delta = position - _lastPointer;
            _restYaw += delta.X * 0.012f;
            _restPitch = Math.Clamp(_restPitch - delta.Y * 0.012f, -1.45f, 1.45f);
            _lastPointer = position;
            SetTarget(_restPitch, _restYaw);
            args.Handled = true;
        }
        else if (args.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse && _settings.AnimationsEnabled)
        {
            var x = Math.Clamp((float)(point.X / Math.Max(1, ActualWidth)) * 2f - 1f, -1f, 1f);
            var y = Math.Clamp((float)(point.Y / Math.Max(1, ActualHeight)) * 2f - 1f, -1f, 1f);
            SetTarget(_restPitch - y * 0.15f, _restYaw + x * 0.22f);
        }
    }

    private void PointerReleasedOnSculpture(object sender, PointerRoutedEventArgs args)
    {
        if (!_dragging || args.Pointer.PointerId != _capturedPointer) return;
        _dragging = false;
        ReleasePointerCapture(args.Pointer);
        SetTarget(_restPitch, _restYaw);
        args.Handled = true;
    }

    private void PointerCaptureLostOnSculpture(object sender, PointerRoutedEventArgs args)
    {
        _dragging = false;
        if (IsLoaded) SetTarget(_restPitch, _restYaw);
    }

    private void PointerExitedOnSculpture(object sender, PointerRoutedEventArgs args)
    {
        if (!_dragging) SetTarget(_restPitch, _restYaw);
    }

    private void KeyDownOnSculpture(object sender, KeyRoutedEventArgs args)
    {
        switch (args.Key)
        {
            case global::Windows.System.VirtualKey.Left: _restYaw -= 0.22f; break;
            case global::Windows.System.VirtualKey.Right: _restYaw += 0.22f; break;
            case global::Windows.System.VirtualKey.Up: _restPitch = Math.Clamp(_restPitch - 0.22f, -1.45f, 1.45f); break;
            case global::Windows.System.VirtualKey.Down: _restPitch = Math.Clamp(_restPitch + 0.22f, -1.45f, 1.45f); break;
            case global::Windows.System.VirtualKey.Home: _restPitch = DefaultPitch; _restYaw = DefaultYaw; break;
            default: return;
        }
        SetTarget(_restPitch, _restYaw);
        args.Handled = true;
    }

    private void SetTarget(float pitch, float yaw)
    {
        if (_canvas is null) return;
        _targetPitch = pitch;
        _targetYaw = yaw;
        if (!_settings.AnimationsEnabled)
        {
            _timer.Stop();
            _pitch = pitch; _yaw = yaw;
            _canvas.Invalidate();
            return;
        }
        if (_timer.IsRunning) return;
        _lastTick = Stopwatch.GetTimestamp();
        _timer.Start();
    }

    private void Tick(DispatcherQueueTimer sender, object args)
    {
        if (_canvas is null) { sender.Stop(); return; }
        var now = Stopwatch.GetTimestamp();
        var elapsed = Math.Clamp((now - _lastTick) / (float)Stopwatch.Frequency, 0, 0.1f);
        _lastTick = now;
        var amount = _settings.AnimationsEnabled ? 1f - MathF.Exp(-18f * elapsed) : 1f;
        _pitch += (_targetPitch - _pitch) * amount;
        _yaw += (_targetYaw - _yaw) * amount;
        if (MathF.Abs(_targetPitch - _pitch) + MathF.Abs(_targetYaw - _yaw) < 0.001f)
        {
            _pitch = _targetPitch; _yaw = _targetYaw;
            sender.Stop();
        }
        _canvas.Invalidate();
    }

    private readonly record struct Bead(Vector3 Position, float Radius, int Palette);
    private readonly record struct Face(Vector3 A, Vector3 B, Vector3 C, int Palette, bool Ribbon);
    private readonly record struct DrawItem(float Depth, Vector2 A, Vector2 B, Vector2 C, float Radius, int Palette, Color Color, bool IsBead);
    private sealed class DrawItemComparer : IComparer<DrawItem>
    {
        public static readonly DrawItemComparer Instance = new();
        public int Compare(DrawItem x, DrawItem y) => x.Depth.CompareTo(y.Depth);
    }
}
