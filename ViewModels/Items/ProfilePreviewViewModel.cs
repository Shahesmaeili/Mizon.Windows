using Symbol = FluentIcons.Common.Symbol;
using Microsoft.UI.Xaml.Media;
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
}

public enum ProfilePreviewKind { Client, Person }
public enum ProfilePreviewAction { Primary, Secondary }

public sealed class ProfilePreviewActionEventArgs(ProfilePreviewViewModel profile, ProfilePreviewAction action) : EventArgs
{
    public ProfilePreviewViewModel Profile { get; } = profile;
    public ProfilePreviewAction Action { get; } = action;
}
