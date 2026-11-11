namespace Mizon.Windows.Services;

internal interface IWindowPresentationHost
{
    bool IsAvailable { get; }
    bool IsFullScreen { get; }
    bool IsFullScreenControlsVisible { get; }
    void SetFullScreen(bool enabled);
}
