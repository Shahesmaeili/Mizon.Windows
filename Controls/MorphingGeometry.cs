using FluentIcons.Common;
using FluentIcons.WinUI;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Windows.UI;
using FluentSymbol = FluentIcons.Common.Symbol;
using FluentSymbolIcon = FluentIcons.WinUI.SymbolIcon;

namespace Mizon.Windows.Controls;

/// <summary>Animates between SVG paths or FluentIcons symbols when IsSecondary changes.</summary>
public sealed class MorphingGeometry : UserControl
{
    private readonly CanvasControl _canvas;
    private readonly DispatcherQueueTimer _timer;
    private CanvasGeometry? _primaryGeometry, _secondaryGeometry;
    private List<Vector2>? _primaryPoints, _secondaryPoints;
    private string? _cachedPrimarySource, _cachedSecondarySource;
    private int _cachedPointCount;
    private float _progress, _startProgress, _targetProgress;
    private DateTimeOffset _transitionStartedAt;

    public MorphingGeometry()
    {
        _canvas = new CanvasControl { ClearColor = Color.FromArgb(0, 0, 0, 0) };
        _canvas.Draw += Canvas_Draw;
        Content = _canvas;
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.Tick += Timer_Tick;
        Unloaded += (_, _) => { _timer.Stop(); ResetGeometry(); };
    }

    public string? PrimaryData { get => (string?)GetValue(PrimaryDataProperty); set => SetValue(PrimaryDataProperty, value); }
    public static readonly DependencyProperty PrimaryDataProperty = DependencyProperty.Register(nameof(PrimaryData), typeof(string), typeof(MorphingGeometry), new PropertyMetadata(null, OnGeometryDataChanged));
    public string? SecondaryData { get => (string?)GetValue(SecondaryDataProperty); set => SetValue(SecondaryDataProperty, value); }
    public static readonly DependencyProperty SecondaryDataProperty = DependencyProperty.Register(nameof(SecondaryData), typeof(string), typeof(MorphingGeometry), new PropertyMetadata(null, OnGeometryDataChanged));
    public FluentSymbol? PrimarySymbol { get => (FluentSymbol?)GetValue(PrimarySymbolProperty); set => SetValue(PrimarySymbolProperty, value); }
    public static readonly DependencyProperty PrimarySymbolProperty = DependencyProperty.Register(nameof(PrimarySymbol), typeof(FluentSymbol?), typeof(MorphingGeometry), new PropertyMetadata(null, OnGeometryDataChanged));
    public FluentSymbol? SecondarySymbol { get => (FluentSymbol?)GetValue(SecondarySymbolProperty); set => SetValue(SecondarySymbolProperty, value); }
    public static readonly DependencyProperty SecondarySymbolProperty = DependencyProperty.Register(nameof(SecondarySymbol), typeof(FluentSymbol?), typeof(MorphingGeometry), new PropertyMetadata(null, OnGeometryDataChanged));
    public IconVariant IconVariant { get => (IconVariant)GetValue(IconVariantProperty); set => SetValue(IconVariantProperty, value); }
    public static readonly DependencyProperty IconVariantProperty = DependencyProperty.Register(nameof(IconVariant), typeof(IconVariant), typeof(MorphingGeometry), new PropertyMetadata(IconVariant.Regular, OnGeometryDataChanged));
    public bool IsSecondary { get => (bool)GetValue(IsSecondaryProperty); set => SetValue(IsSecondaryProperty, value); }
    public static readonly DependencyProperty IsSecondaryProperty = DependencyProperty.Register(nameof(IsSecondary), typeof(bool), typeof(MorphingGeometry), new PropertyMetadata(false, OnIsSecondaryChanged));
    public Color FillColor { get => (Color)GetValue(FillColorProperty); set => SetValue(FillColorProperty, value); }
    public static readonly DependencyProperty FillColorProperty = DependencyProperty.Register(nameof(FillColor), typeof(Color), typeof(MorphingGeometry), new PropertyMetadata(Color.FromArgb(255, 255, 255, 255), OnVisualPropertyChanged));
    public TimeSpan AnimationDuration { get => (TimeSpan)GetValue(AnimationDurationProperty); set => SetValue(AnimationDurationProperty, value); }
    public static readonly DependencyProperty AnimationDurationProperty = DependencyProperty.Register(nameof(AnimationDuration), typeof(TimeSpan), typeof(MorphingGeometry), new PropertyMetadata(TimeSpan.FromMilliseconds(220)));
    public int PointCount { get => (int)GetValue(PointCountProperty); set => SetValue(PointCountProperty, value); }
    public static readonly DependencyProperty PointCountProperty = DependencyProperty.Register(nameof(PointCount), typeof(int), typeof(MorphingGeometry), new PropertyMetadata(300, OnGeometryDataChanged));

    private static void OnGeometryDataChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) { var control = (MorphingGeometry)sender; control.ResetGeometry(); control._canvas.Invalidate(); }
    private static void OnVisualPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((MorphingGeometry)sender)._canvas.Invalidate();
    private static void OnIsSecondaryChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (MorphingGeometry)sender;
        control._startProgress = control._progress;
        control._targetProgress = (bool)args.NewValue ? 1f : 0f;
        control._transitionStartedAt = DateTimeOffset.UtcNow;
        control._timer.Start();
    }

    private void Timer_Tick(DispatcherQueueTimer sender, object args)
    {
        var t = Math.Clamp((float)((DateTimeOffset.UtcNow - _transitionStartedAt).TotalMilliseconds / Math.Max(1, AnimationDuration.TotalMilliseconds)), 0f, 1f);
        _progress = _startProgress + ((_targetProgress - _startProgress) * (t * t * (3f - (2f * t))));
        _canvas.Invalidate();
        if (t >= 1f) _timer.Stop();
    }

    private void Canvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (!EnsureGeometry(sender) || _primaryPoints is null || _secondaryPoints is null) return;
        var points = FitToCanvas(Morph(_primaryPoints, _secondaryPoints, _progress), (float)sender.ActualWidth, (float)sender.ActualHeight);
        using var builder = new CanvasPathBuilder(sender);
        builder.BeginFigure(points[0]);
        for (var index = 1; index < points.Count; index++) builder.AddLine(points[index]);
        builder.EndFigure(CanvasFigureLoop.Closed);
        using var geometry = CanvasGeometry.CreatePath(builder);
        args.DrawingSession.FillGeometry(geometry, FillColor);
    }

    private bool EnsureGeometry(CanvasControl canvas)
    {
        var primarySource = GetSourceKey(PrimaryData, PrimarySymbol);
        var secondarySource = GetSourceKey(SecondaryData, SecondarySymbol);
        if (primarySource is null || secondarySource is null) return false;
        if (_cachedPrimarySource == primarySource && _cachedSecondarySource == secondarySource && _cachedPointCount == PointCount && _primaryGeometry is not null && _secondaryGeometry is not null) return true;
        ResetGeometry();
        _primaryGeometry = CreateGeometry(canvas, PrimaryData, PrimarySymbol);
        _secondaryGeometry = CreateGeometry(canvas, SecondaryData, SecondarySymbol);
        var count = Math.Max(16, PointCount);
        _primaryPoints = Sample(_primaryGeometry, count); _secondaryPoints = Sample(_secondaryGeometry, count);
        _cachedPrimarySource = primarySource; _cachedSecondarySource = secondarySource; _cachedPointCount = PointCount;
        return true;
    }

    private string? GetSourceKey(string? data, FluentSymbol? symbol) =>
        !string.IsNullOrWhiteSpace(data) ? $"svg:{data}" : symbol is { } value ? $"symbol:{value}:{IconVariant}" : null;

    private CanvasGeometry CreateGeometry(CanvasControl canvas, string? data, FluentSymbol? symbol)
    {
        if (!string.IsNullOrWhiteSpace(data)) return SvgPathParser.CreateGeometry(canvas, data);
        if (symbol is not { } value) throw new InvalidOperationException("Either path data or a FluentIcons symbol is required.");

        var resolver = new SymbolGlyphResolver { Symbol = value, IconVariant = IconVariant };
        using var layout = new CanvasTextLayout(canvas, resolver.GlyphText, new CanvasTextFormat
        {
            FontFamily = resolver.Font.Source,
            FontSize = 100f
        }, 0f, 0f);
        return CanvasGeometry.CreateText(layout);
    }

    private void ResetGeometry()
    {
        _primaryGeometry?.Dispose(); _secondaryGeometry?.Dispose();
        _primaryGeometry = null; _secondaryGeometry = null; _primaryPoints = null; _secondaryPoints = null;
        _cachedPrimarySource = null; _cachedSecondarySource = null;
    }

    private static List<Vector2> Sample(CanvasGeometry geometry, int count)
    {
        var length = geometry.ComputePathLength(); var result = new List<Vector2>(count);
        for (var index = 0; index < count; index++) result.Add(geometry.ComputePointOnPath((index / (float)(count - 1)) * length));
        return result;
    }
    private static List<Vector2> Morph(IReadOnlyList<Vector2> primary, IReadOnlyList<Vector2> secondary, float progress)
    {
        var result = new List<Vector2>(primary.Count);
        for (var index = 0; index < primary.Count; index++) result.Add(Vector2.Lerp(primary[index], secondary[index], progress));
        return result;
    }
    private static List<Vector2> FitToCanvas(IReadOnlyList<Vector2> points, float width, float height, float padding = 8f)
    {
        var minX = points[0].X; var maxX = minX; var minY = points[0].Y; var maxY = minY;
        foreach (var point in points) { minX = Math.Min(minX, point.X); maxX = Math.Max(maxX, point.X); minY = Math.Min(minY, point.Y); maxY = Math.Max(maxY, point.Y); }
        var geometryWidth = maxX - minX; var geometryHeight = maxY - minY; var availableWidth = width - (padding * 2); var availableHeight = height - (padding * 2);
        var scale = geometryWidth > 0 && geometryHeight > 0 ? Math.Min(availableWidth / geometryWidth, availableHeight / geometryHeight) : 1f;
        var offsetX = padding + ((availableWidth - (geometryWidth * scale)) / 2) - (minX * scale); var offsetY = padding + ((availableHeight - (geometryHeight * scale)) / 2) - (minY * scale);
        var result = new List<Vector2>(points.Count);
        foreach (var point in points) result.Add(new Vector2((point.X * scale) + offsetX, (point.Y * scale) + offsetY));
        return result;
    }

    private sealed class SymbolGlyphResolver : FluentSymbolIcon
    {
        public string GlyphText => IconText;
        public Microsoft.UI.Xaml.Media.FontFamily Font => IconFont;
    }
}

internal static class SvgPathParser
{
    private static readonly Regex TokenRegex = new("[a-zA-Z]|[-+]?(?:\\d+\\.?\\d*|\\.\\d+)(?:[eE][-+]?\\d+)?", RegexOptions.Compiled);

    public static CanvasGeometry CreateGeometry(ICanvasResourceCreator resourceCreator, string data)
    {
        var tokens = TokenRegex.Matches(data).Select(match => match.Value).ToArray();
        using var builder = new CanvasPathBuilder(resourceCreator);
        var index = 0; var command = '\0'; var current = Vector2.Zero; var start = Vector2.Zero; var hasFigure = false;
        var lastCubic = Vector2.Zero; var lastQuadratic = Vector2.Zero; var previous = '\0';
        while (index < tokens.Length)
        {
            if (char.IsLetter(tokens[index][0])) command = tokens[index++][0];
            if (command == '\0') throw new FormatException("SVG path data must start with a command.");
            var relative = char.IsLower(command); var operation = char.ToUpperInvariant(command);
            if (operation == 'Z') { if (hasFigure) { builder.EndFigure(CanvasFigureLoop.Closed); current = start; hasFigure = false; } previous = command; command = '\0'; continue; }
            do
            {
                Vector2 Point() => ReadPoint(tokens, ref index, relative ? current : Vector2.Zero);
                switch (operation)
                {
                    case 'M':
                        var move = Point(); if (hasFigure) builder.EndFigure(CanvasFigureLoop.Open);
                        builder.BeginFigure(move); current = start = move; hasFigure = true; operation = 'L'; break;
                    case 'L': current = Point(); builder.AddLine(current); break;
                    case 'H': current.X = ReadNumber(tokens, ref index) + (relative ? current.X : 0); builder.AddLine(current); break;
                    case 'V': current.Y = ReadNumber(tokens, ref index) + (relative ? current.Y : 0); builder.AddLine(current); break;
                    case 'C': var c1 = Point(); var c2 = Point(); current = Point(); builder.AddCubicBezier(c1, c2, current); lastCubic = c2; break;
                    case 'S': var c = (char.ToUpperInvariant(previous) is 'C' or 'S') ? (2 * current) - lastCubic : current; var cEnd = Point(); current = Point(); builder.AddCubicBezier(c, cEnd, current); lastCubic = cEnd; break;
                    case 'Q': var q = Point(); current = Point(); builder.AddQuadraticBezier(q, current); lastQuadratic = q; break;
                    case 'T': var qReflect = (char.ToUpperInvariant(previous) is 'Q' or 'T') ? (2 * current) - lastQuadratic : current; current = Point(); builder.AddQuadraticBezier(qReflect, current); lastQuadratic = qReflect; break;
                    case 'A':
                        var radiusX = ReadNumber(tokens, ref index); var radiusY = ReadNumber(tokens, ref index); var angle = ReadNumber(tokens, ref index) * (MathF.PI / 180f);
                        var largeArc = ReadNumber(tokens, ref index) != 0; var clockwise = ReadNumber(tokens, ref index) != 0; current = Point();
                        builder.AddArc(current, radiusX, radiusY, angle, clockwise ? CanvasSweepDirection.Clockwise : CanvasSweepDirection.CounterClockwise, largeArc ? CanvasArcSize.Large : CanvasArcSize.Small); break;
                    default: throw new NotSupportedException($"SVG command '{command}' is not supported.");
                }
                previous = command;
            } while (index < tokens.Length && !char.IsLetter(tokens[index][0]));
        }
        if (hasFigure) builder.EndFigure(CanvasFigureLoop.Open);
        return CanvasGeometry.CreatePath(builder);
    }

    private static Vector2 ReadPoint(IReadOnlyList<string> tokens, ref int index, Vector2 offset) => new(ReadNumber(tokens, ref index) + offset.X, ReadNumber(tokens, ref index) + offset.Y);
    private static float ReadNumber(IReadOnlyList<string> tokens, ref int index)
    {
        if (index >= tokens.Count || char.IsLetter(tokens[index][0])) throw new FormatException("Incomplete SVG path command.");
        return float.Parse(tokens[index++], CultureInfo.InvariantCulture);
    }
}
