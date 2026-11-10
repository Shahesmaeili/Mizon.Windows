using Mizon.API.Client.Responses.Musics;
using RGB.NET.Core;
using RGB.NET.Devices.OpenRGB;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

/// <summary>
/// Drives the OpenRGB surface from server-authored waveform frames. It never reads the
/// local audio stream, so every device renders the same beat envelope for a given track.
/// </summary>
public sealed class RgbMusicLightingService : IDisposable
{
    private const int TargetFrameIntervalMilliseconds = 33;
    private readonly List<WaveformResponse> _waveforms = [];
    private readonly List<RgbColor> _palette = [];
    private static readonly RgbColor PausedColor = new(230, 255, 13);
    private readonly Channel<RgbColor> _outputFrames = Channel.CreateBounded<RgbColor>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        SingleWriter = true,
        FullMode = BoundedChannelFullMode.DropOldest,
        AllowSynchronousContinuations = false
    });
    private Task? _outputTask;
    private Microsoft.UI.Xaml.Window? _shutdownWindow;
    private volatile bool _isAvailable;
    private int _disposed;
    private float _maximumRms = 1f;
    private float _maximumTransient = 1f;
    private const double MinimumPlaybackBrightness = 0.40d;
    private double _smoothedBrightness = MinimumPlaybackBrightness;
    private long _lastFrameTick;
    private bool _initializationAttempted;
    private bool _isPaused;

    public bool IsAvailable => _isAvailable && Volatile.Read(ref _disposed) == 0;

    public void Configure(MusicLightingProfileResponse profile, IEnumerable<WaveformResponse> waveforms)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        _isPaused = false;
        _waveforms.Clear();
        _waveforms.AddRange(waveforms.OrderBy(x => x.OffsetInMilliseconds));
        _maximumRms = Math.Max(0.0001f, _waveforms.Count == 0 ? 1f : _waveforms.Max(x => Math.Abs(x.RMS)));
        _maximumTransient = Math.Max(0.0001f, _waveforms.Count == 0 ? 1f : _waveforms.Max(x => Math.Abs(x.Transient)));

        _palette.Clear();
        if (TryParseColor(profile.PrimaryColor, out var primaryColor))
            _palette.Add(primaryColor);

        if (_palette.Count == 0)
            _palette.Add(new(0, 180, 255));

        _smoothedBrightness = MinimumPlaybackBrightness;
        EnsureInitialized();
    }

    public void Update(TimeSpan position, bool isPlaying)
    {
        if (!IsAvailable || _isPaused)
            return;

        var tick = Environment.TickCount64;
        if (tick - _lastFrameTick < TargetFrameIntervalMilliseconds)
            return;

        _lastFrameTick = tick;
        var targetBrightness = isPlaying ? GetTargetBrightness(position) : MinimumPlaybackBrightness;
        // The waveform is produced at roughly 40ms intervals. A fast attack and a
        // noticeably slower release make transients visible instead of averaging them away.
        var smoothing = targetBrightness > _smoothedBrightness ? 0.70d : 0.32d;
        _smoothedBrightness += (targetBrightness - _smoothedBrightness) * smoothing;
        ApplyFrame(GetPrimaryColor(), _smoothedBrightness);
    }

    public void SetIdle()
    {
        _isPaused = false;
        Update(TimeSpan.Zero, isPlaying: false);
    }

    public void Resume()
    {
        _isPaused = false;
        _smoothedBrightness = MinimumPlaybackBrightness;
        _lastFrameTick = 0;
    }

    public void SetPaused()
    {
        _isPaused = true;
        if (!IsAvailable)
            return;

        _smoothedBrightness = 1d;
        _lastFrameTick = Environment.TickCount64;
        ApplyFrame(PausedColor, 1d);
    }

    private void EnsureInitialized()
    {
        if (_initializationAttempted || Volatile.Read(ref _disposed) != 0)
            return;

        _initializationAttempted = true;
        _shutdownWindow = App.MainMizonWindow;
        if (_shutdownWindow is not null)
            _shutdownWindow.Closed += MainWindow_Closed;

        RGBSurface? surface = null;
        try
        {
            var definition = new OpenRGBServerDefinition
            {
                Ip = "127.0.0.1",
                Port = 6742,
                ClientName = "Mizon"
            };

            OpenRGBDeviceProvider.Instance.AddDeviceDefinition(definition);
            OpenRGBDeviceProvider.Instance.Initialize(throwExceptions: false);

            surface = new RGBSurface();
            surface.Attach(OpenRGBDeviceProvider.Instance.Devices);
            _isAvailable = surface.Devices.Any();
            var outputSurface = surface;
            _outputTask = Task.Run(() => ProcessOutputFramesAsync(outputSurface));
            surface = null; // The output worker now owns cleanup, including during shutdown.
        }
        catch (Exception exception)
        {
            // OpenRGB is optional. A missing server/device must never interrupt music playback.
            AppLog.Error("OpenRGB initialization failed.", exception);
            _isAvailable = false;
            surface?.Dispose();
        }
    }

    private double GetTargetBrightness(TimeSpan position)
    {
        if (_waveforms.Count == 0)
            return 0.75d;

        var index = FindWaveformIndex((int)Math.Max(0d, position.TotalMilliseconds));
        var frame = _waveforms[index];
        var rms = Math.Clamp(Math.Abs(frame.RMS) / _maximumRms, 0f, 1f);
        var transient = Math.Clamp(Math.Abs(frame.Transient) / _maximumTransient, 0f, 1f);
        // Keep a deliberately wide 40-100% envelope. RMS supplies the body of the
        // animation while the strongly weighted transient makes every beat obvious.
        return Math.Clamp(MinimumPlaybackBrightness + (Math.Pow(rms, 1.70d) * 0.28d) + (Math.Pow(transient, 0.60d) * 0.60d), MinimumPlaybackBrightness, 1d);
    }

    private RgbColor GetPrimaryColor()
    {
        return _palette[0];
    }

    private void ApplyFrame(RgbColor color, double brightness)
    {
        if (!IsAvailable)
            return;

        // Preserve the PrimaryAccentColor hue, but normalize its value so a naturally dark
        // cover color can still drive physical LEDs at the requested 40-100% brightness.
        var maximumChannel = Math.Max(1, Math.Max((int)color.Red, Math.Max((int)color.Green, (int)color.Blue)));
        var colorScale = 255d / maximumChannel;
        var red = (byte)Math.Round(Math.Min(255d, color.Red * colorScale * brightness));
        var green = (byte)Math.Round(Math.Min(255d, color.Green * colorScale * brightness));
        var blue = (byte)Math.Round(Math.Min(255d, color.Blue * colorScale * brightness));
        // A slow driver retains only the latest pending color; UI/audio callbacks
        // never wait for hardware I/O and never create a task for each frame.
        _outputFrames.Writer.TryWrite(new RgbColor(red, green, blue));
    }

    private async Task ProcessOutputFramesAsync(RGBSurface surface)
    {
        try
        {
            await foreach (var color in _outputFrames.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                if (Volatile.Read(ref _disposed) != 0)
                    break;

                foreach (var device in surface.Devices)
                    foreach (var led in device)
                        led.Color = new RGB.NET.Core.Color(255, (int)color.Red, (int)color.Green, (int)color.Blue);

                surface.Update();
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("OpenRGB frame output failed.", exception);
        }
        finally
        {
            _isAvailable = false;
            _outputFrames.Writer.TryComplete();
            try { surface.Dispose(); }
            catch (Exception exception) { AppLog.Error("OpenRGB surface cleanup failed.", exception); }
        }
    }

    private void MainWindow_Closed(object? sender, Microsoft.UI.Xaml.WindowEventArgs args) => Dispose();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _isAvailable = false;
        _outputFrames.Writer.TryComplete();
        var window = _shutdownWindow;
        _shutdownWindow = null;
        if (window is not null)
        {
            if (window.DispatcherQueue.HasThreadAccess)
                window.Closed -= MainWindow_Closed;
            else
                window.DispatcherQueue.TryEnqueue(() => window.Closed -= MainWindow_Closed);
        }
    }

    private int FindWaveformIndex(int offsetInMilliseconds)
    {
        var low = 0;
        var high = _waveforms.Count - 1;
        while (low < high)
        {
            var middle = low + ((high - low + 1) / 2);
            if (_waveforms[middle].OffsetInMilliseconds <= offsetInMilliseconds)
                low = middle;
            else
                high = middle - 1;
        }

        return low;
    }

    private static bool TryParseColor(string? value, out RgbColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var hex = value.Trim().TrimStart('#');
        if (hex.Length == 8)
            hex = hex[2..];
        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return false;

        color = new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    private readonly record struct RgbColor(byte Red, byte Green, byte Blue);
}
