using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

// Accessed on the UI dispatcher. Sessions borrow playback; disposing a view must
// never dispose or stop the application's player.
public interface IPictureInPictureSession : IDisposable
{
    string Title { get; }
    string Subtitle { get; }
    string? ArtworkPath { get; }
    string? AccentColor { get; }
    bool IsVideo { get; }
    object? MediaIdentity { get; }
    bool IsPlaying { get; }
    bool IsBusy { get; }
    bool CanSeek { get; }
    bool CanNext { get; }
    bool CanPrevious { get; }
    TimeSpan Position { get; }
    TimeSpan Duration { get; }
    double Volume { get; set; } // 0..1
    float Intensity { get; }
    Task ToggleAsync();
    Task SeekAsync(TimeSpan position);
    Task NextAsync();
    Task PreviousAsync();
    void ReturnToApp();
    void AttachVideo(MediaPlayerElement surface);
    void DetachVideo();
}
