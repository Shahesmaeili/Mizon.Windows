using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Mizon.Windows.Services;

internal sealed record AppActionEntry(
    string Id,
    string Destination,
    string LanguageTag,
    bool RequiresSignIn);

internal static class AppActionCatalog
{
    internal const string ProviderClsid = "25F10A63-B41E-4B39-97AD-7B781CA81D89";

    // Separate registered variants let availability follow Mizon's selected language.
    // The original Persian account IDs remain stable for existing callers.
    internal static IReadOnlyList<AppActionEntry> Entries { get; } = Array.AsReadOnly(
        new AppActionEntry[]
        {
            new("Mizon.OpenLogin", "login", "fa-IR", false),
            new("Mizon.OpenRegister", "register", "fa-IR", false),
            new("Mizon.OpenMusic", "music", "fa-IR", true),
            new("Mizon.OpenMovies", "movies", "fa-IR", true),
            new("Mizon.OpenApps", "apps", "fa-IR", true),
            new("Mizon.OpenVpn", "vpn", "fa-IR", true),
            new("Mizon.OpenLogin.English", "login", "en-US", false),
            new("Mizon.OpenRegister.English", "register", "en-US", false),
            new("Mizon.OpenMusic.English", "music", "en-US", true),
            new("Mizon.OpenMovies.English", "movies", "en-US", true),
            new("Mizon.OpenApps.English", "apps", "en-US", true),
            new("Mizon.OpenVpn.English", "vpn", "en-US", true)
        });

    internal static bool TryResolve(string? id, [NotNullWhen(true)] out AppActionEntry? entry)
    {
        foreach (var candidate in Entries)
        {
            if (string.Equals(candidate.Id, id, StringComparison.Ordinal))
            {
                entry = candidate;
                return true;
            }
        }
        entry = null;
        return false;
    }

    internal static bool IsAvailable(AppActionEntry entry, bool signedIn, string languageTag) =>
        entry.RequiresSignIn == signedIn
        && string.Equals(entry.LanguageTag, languageTag, StringComparison.OrdinalIgnoreCase);
}
