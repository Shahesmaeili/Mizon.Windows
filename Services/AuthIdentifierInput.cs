using System;

namespace Mizon.Windows.Services;

internal static class AuthIdentifierInput
{
    internal static bool IsUsername(string? text) => IsAllowed(text, allowEmailCharacters: false);
    internal static bool IsLogin(string? text) => IsAllowed(text, allowEmailCharacters: true);

    private static bool IsAllowed(string? text, bool allowEmailCharacters)
    {
        foreach (var character in text.AsSpan())
        {
            if (char.IsAsciiLetterOrDigit(character) || character is '.' or '_') continue;
            if (allowEmailCharacters && character is '@' or '+' or '%' or '-') continue;
            return false;
        }
        return true;
    }
}
