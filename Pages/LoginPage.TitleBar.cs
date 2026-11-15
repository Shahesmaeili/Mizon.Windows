using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace Mizon.Windows.Pages;

public sealed partial class LoginPage
{
    private InputNonClientPointerSource? _languagePointerSource;
    private RectInt32? _languageRegion;
    private bool _languageInCaption;

    private void AttachLanguagePicker()
    {
        if (_hostAppWindow is null) return;
        try
        {
            _languagePointerSource = InputNonClientPointerSource.GetForWindowId(_hostAppWindow.Id);
            LanguagePickerHost.LayoutUpdated += LanguagePickerLayoutUpdated;
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            Services.AppLog.Information($"Login title bar input is unavailable ({exception.GetType().Name}).");
        }
    }

    private void UpdateLanguagePickerPlacement(double width, double captionHeight)
    {
        var scale = XamlRoot?.RasterizationScale ?? 1;
        var rightInset = (_hostAppWindow?.TitleBar.RightInset ?? 0) / scale;
        var leftInset = (_hostAppWindow?.TitleBar.LeftInset ?? 0) / scale;
        var pickerWidth = LanguagePickerHost.Width;
        _languageInCaption = _languagePointerSource is not null && captionHeight >= 36
            && width - leftInset - rightInset >= pickerWidth + 24;
        Grid.SetRow(LanguagePickerHost, _languageInCaption ? 0 : 1);
        LanguagePickerHost.VerticalAlignment = _languageInCaption ? VerticalAlignment.Center : VerticalAlignment.Top;
        var margin = _languageInCaption ? new Thickness(leftInset + 12, 0, 0, 0) : new Thickness(12, 6, 0, 0);
        if (LanguagePickerHost.Margin != margin) LanguagePickerHost.Margin = margin;
        var brandAlignment = _languageInCaption ? HorizontalAlignment.Center : HorizontalAlignment.Right;
        if (CompactBrand.HorizontalAlignment != brandAlignment) CompactBrand.HorizontalAlignment = brandAlignment;
    }

    private void LanguagePickerLayoutUpdated(object? sender, object args)
    {
        if (_languagePointerSource is not { } pointer || !IsLoaded || _hostAppWindow is not { } window) return;
        try
        {
            var titleBar = window.TitleBar;
            RectInt32? region = null;
            if (_languageInCaption && Common.TitleBarRegions.GetPixelBounds(LanguagePickerHost) is { } bounds)
            {
                var width = (int)(ActualWidth * (XamlRoot?.RasterizationScale ?? 1));
                var caption = new RectInt32(titleBar.LeftInset, 0,
                    Math.Max(0, width - titleBar.LeftInset - titleBar.RightInset), titleBar.Height);
                region = Common.TitleBarRegions.Intersect(bounds, caption);
            }
            if (Nullable.Equals(_languageRegion, region)) return;

            if (region is { } value) pointer.SetRegionRects(NonClientRegionKind.Passthrough, [value]);
            else pointer.ClearRegionRects(NonClientRegionKind.Passthrough);
            _languageRegion = region;
        }
        catch (Exception exception) when (exception is COMException or ArgumentException or InvalidOperationException)
        {
            Services.AppLog.Information($"Login title bar input could not update ({exception.GetType().Name}).");
            DetachLanguagePicker();
            UpdateLanguagePickerPlacement(ActualWidth, GetCaptionHeight());
        }
    }

    private void DetachLanguagePicker()
    {
        LanguagePickerHost.LayoutUpdated -= LanguagePickerLayoutUpdated;
        try
        {
            if (_languageRegion.HasValue) _languagePointerSource?.ClearRegionRects(NonClientRegionKind.Passthrough);
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException) { }
        _languagePointerSource = null;
        _languageRegion = null;
    }
}
