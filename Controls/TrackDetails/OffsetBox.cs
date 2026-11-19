// Adapted from DevWinUI 10.4.1 (30a7bbb), MIT. See LICENSE.DevWinUI.txt.
using System;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using Microsoft.UI.Xaml.Input;

namespace Mizon.Windows.Controls.TrackDetails;

public partial class OffsetBox : ContentControl
{
    private readonly global::Windows.UI.ViewManagement.UISettings _settings = new();

    public OffsetBox() => Unloaded += (_, _) => ResetOffset();

    private void ResetOffset()
    {
        if (Content is UIElement element)
        {
            element.TranslationTransition = null;
            element.Translation = NormalValue + (ShowShadowOnHover ? NormalShadowTranslation : Vector3.Zero);
        }
    }

    public bool ShowShadowOnHover
    {
        get { return (bool)GetValue(ShowShadowOnHoverProperty); }
        set { SetValue(ShowShadowOnHoverProperty, value); }
    }

    public static readonly DependencyProperty ShowShadowOnHoverProperty =
        DependencyProperty.Register(nameof(ShowShadowOnHover), typeof(bool), typeof(OffsetBox), new PropertyMetadata(true, OnShowShadowOnHoverChanged));

    private static void OnShowShadowOnHoverChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (OffsetBox)d;
        if (ctl != null)
        {
            ctl.UpdateShowShadowOnHover();
        }
    }

    public Vector3 HoverValue
    {
        get { return (Vector3)GetValue(HoverValueProperty); }
        set { SetValue(HoverValueProperty, value); }
    }

    public static readonly DependencyProperty HoverValueProperty =
        DependencyProperty.Register(nameof(HoverValue), typeof(Vector3), typeof(OffsetBox), new PropertyMetadata(new Vector3(0, -5f, 0)));

    public Vector3 NormalValue
    {
        get { return (Vector3)GetValue(NormalValueProperty); }
        set { SetValue(NormalValueProperty, value); }
    }

    public static readonly DependencyProperty NormalValueProperty =
        DependencyProperty.Register(nameof(NormalValue), typeof(Vector3), typeof(OffsetBox), new PropertyMetadata(new Vector3(0, 0f, 0)));

    public TimeSpan HoverDuration
    {
        get { return (TimeSpan)GetValue(HoverDurationProperty); }
        set { SetValue(HoverDurationProperty, value); }
    }

    public static readonly DependencyProperty HoverDurationProperty =
        DependencyProperty.Register(nameof(HoverDuration), typeof(TimeSpan), typeof(OffsetBox), new PropertyMetadata(TimeSpan.FromMilliseconds(150)));

    public TimeSpan NormalDuration
    {
        get { return (TimeSpan)GetValue(NormalDurationProperty); }
        set { SetValue(NormalDurationProperty, value); }
    }

    public static readonly DependencyProperty NormalDurationProperty =
        DependencyProperty.Register(nameof(NormalDuration), typeof(TimeSpan), typeof(OffsetBox), new PropertyMetadata(TimeSpan.FromMilliseconds(150)));

    public Vector3 HoverShadowTranslation
    {
        get { return (Vector3)GetValue(HoverShadowTranslationProperty); }
        set { SetValue(HoverShadowTranslationProperty, value); }
    }

    public static readonly DependencyProperty HoverShadowTranslationProperty =
        DependencyProperty.Register(nameof(HoverShadowTranslation), typeof(Vector3), typeof(OffsetBox), new PropertyMetadata(new Vector3(0, 0, 32)));

    public Vector3 NormalShadowTranslation
    {
        get { return (Vector3)GetValue(NormalShadowTranslationProperty); }
        set { SetValue(NormalShadowTranslationProperty, value); }
    }

    public static readonly DependencyProperty NormalShadowTranslationProperty =
        DependencyProperty.Register(nameof(NormalShadowTranslation), typeof(Vector3), typeof(OffsetBox), new PropertyMetadata(new Vector3(0, 0, 0)));

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (Content is UIElement element)
        {
            if (ShowShadowOnHover)
            {
                element.Shadow = new ThemeShadow();
            }
        }
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        ResetOffset();
        base.OnContentChanged(oldContent, newContent);
        if (Content is UIElement element)
        {
            if (ShowShadowOnHover)
            {
                element.Shadow = new ThemeShadow();
            }
        }
    }

    private void UpdateShowShadowOnHover()
    {
        if (Content is UIElement element)
            element.Shadow = ShowShadowOnHover ? new ThemeShadow() : null;
    }
    private void AnimateHover(bool hover)
    {
        if (Content is not UIElement element) return;

        if (_settings.AnimationsEnabled)
        {
            element.TranslationTransition ??= new Vector3Transition();
            element.TranslationTransition.Duration = hover ? HoverDuration : NormalDuration;
        }
        else
        {
            element.TranslationTransition = null;
        }

        var shadow = ShowShadowOnHover ? (hover ? HoverShadowTranslation : NormalShadowTranslation) : Vector3.Zero;
        element.Translation = (hover ? HoverValue : NormalValue) + shadow;
    }

    protected override void OnPointerEntered(PointerRoutedEventArgs e)
    {
        base.OnPointerEntered(e);
        AnimateHover(true);
    }

    protected override void OnPointerExited(PointerRoutedEventArgs e)
    {
        base.OnPointerExited(e);
        AnimateHover(false);
    }
}
