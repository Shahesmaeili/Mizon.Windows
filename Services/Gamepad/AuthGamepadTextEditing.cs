using System;
using System.Globalization;

namespace Mizon.Windows.Services.Gamepad;

/// <summary>An edit contains only the replacement, never a copy of the field's contents.</summary>
internal readonly record struct AuthGamepadTextEdit(int Start, int Length, string Replacement)
{
    internal int Caret => Start + Replacement.Length;
}

/// <summary>Selection and Unicode boundaries shared by the controller keyboard and its checks.</summary>
internal static class AuthGamepadTextEditing
{
    internal static AuthGamepadTextEdit? Insert(string text, int start, int length, string value, int maxLength = 0)
    {
        if (string.IsNullOrEmpty(value)) return null;
        NormalizeSelection(text, ref start, ref length);
        var available = maxLength <= 0 ? int.MaxValue : Math.Max(0, maxLength - (text.Length - length));
        if (value.Length > available)
        {
            var boundary = FloorBoundary(value, available);
            if (boundary == 0) return null;
            value = value[..boundary];
        }
        return new(start, length, value);
    }

    internal static AuthGamepadTextEdit? Backspace(string text, int start, int length)
    {
        NormalizeSelection(text, ref start, ref length);
        if (length > 0) return new(start, length, string.Empty);
        if (start == 0) return null;
        var previous = FloorBoundary(text, start - 1);
        return new(previous, start - previous, string.Empty);
    }

    internal static int MoveCaret(string text, int start, int length, bool forward)
    {
        NormalizeSelection(text, ref start, ref length);
        if (length > 0) return forward ? start + length : start;
        return forward
            ? CeilingBoundary(text, Math.Min(text.Length, start + 1))
            : FloorBoundary(text, Math.Max(0, start - 1));
    }

    private static void NormalizeSelection(string text, ref int start, ref int length)
    {
        var boundedStart = Math.Clamp(start, 0, text.Length);
        var boundedLength = Math.Clamp(length, 0, text.Length - boundedStart);
        start = FloorBoundary(text, boundedStart);
        var end = boundedLength == 0 ? start : CeilingBoundary(text, boundedStart + boundedLength);
        length = end - start;
    }

    private static int FloorBoundary(string text, int index)
    {
        if (index <= 0) return 0;
        if (index >= text.Length) return text.Length;
        var previous = 0;
        foreach (var boundary in StringInfo.ParseCombiningCharacters(text))
        {
            if (boundary > index) break;
            previous = boundary;
        }
        return previous;
    }

    private static int CeilingBoundary(string text, int index)
    {
        if (index <= 0) return 0;
        if (index >= text.Length) return text.Length;
        foreach (var boundary in StringInfo.ParseCombiningCharacters(text))
            if (boundary >= index) return boundary;
        return text.Length;
    }
}
