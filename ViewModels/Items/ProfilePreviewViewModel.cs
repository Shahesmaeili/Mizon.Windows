using Symbol = FluentIcons.Common.Symbol;
using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Services;
using System;

namespace Mizon.Windows.ViewModels.Items;

/// <summary>
/// A presentation snapshot shared by profile previews throughout the application.
/// The caller supplies only public information and decides what each action does.
/// </summary>
public sealed partial class ProfilePreviewViewModel
{
    public Guid PublicId { get; init; }
    public ProfilePreviewKind Kind { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string Bio { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string FooterText { get; init; } = string.Empty;
    public ImageSource? Avatar { get; init; }
    public ImageSource? Banner { get; init; }
    public string PrimaryActionText { get; init; } = string.Empty;
    public Symbol PrimaryActionSymbol { get; init; } = Symbol.Person;
    public string SecondaryActionText { get; init; } = string.Empty;
    public Symbol SecondaryActionSymbol { get; init; } = Symbol.Copy;

    // Public names and biographies remain content. Optional resource keys are for
    // app-owned labels and are resolved again when an open preview changes language.
    public string? SubtitleResourceKey { get; init; }
    public string? SubtitleSuffix { get; init; }
    public string? PersianSubtitleSuffix { get; init; }
    public string? DetailResourceKey { get; init; }
    public string? DetailArgument { get; init; }
    public string? PersianDetailArgument { get; init; }
    public string? FooterResourceKey { get; init; }
    public string? PrimaryActionResourceKey { get; init; }
    public string? SecondaryActionResourceKey { get; init; }

    public string DisplaySubtitle
    {
        get
        {
            var label = Resolve(SubtitleResourceKey, Subtitle);
            var suffix = LocalizedContent(SubtitleSuffix, PersianSubtitleSuffix);
            return string.IsNullOrWhiteSpace(suffix) ? label
                : string.IsNullOrWhiteSpace(label) ? suffix : label + " · " + suffix;
        }
    }

    public string DisplayDetail => string.IsNullOrWhiteSpace(DetailResourceKey) ? Detail
        : DetailArgument is null && PersianDetailArgument is null
            ? LocalizationService.Current.GetString(DetailResourceKey)
            : LocalizationService.Current.Format(DetailResourceKey, LocalizedContent(DetailArgument, PersianDetailArgument));
    public string DisplayFooterText => Resolve(FooterResourceKey, FooterText);
    public string DisplayPrimaryActionText => Resolve(PrimaryActionResourceKey, PrimaryActionText);
    public string DisplaySecondaryActionText => Resolve(SecondaryActionResourceKey, SecondaryActionText);

    private static string Resolve(string? resourceKey, string fallback) => string.IsNullOrWhiteSpace(resourceKey)
        ? fallback : LocalizationService.Current.GetString(resourceKey);

    private static string LocalizedContent(string? text, string? persianText) =>
        LocalizationService.Current.IsPersian && !string.IsNullOrWhiteSpace(persianText)
            ? persianText : text ?? string.Empty;
}

public enum ProfilePreviewKind { Client, Person }
public enum ProfilePreviewAction { Primary, Secondary }

public sealed class ProfilePreviewActionEventArgs(ProfilePreviewViewModel profile, ProfilePreviewAction action) : EventArgs
{
    public ProfilePreviewViewModel Profile { get; } = profile;
    public ProfilePreviewAction Action { get; } = action;
}
