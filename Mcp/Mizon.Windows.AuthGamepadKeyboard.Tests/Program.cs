using Mizon.Windows.Services.Gamepad;

var assertions = 0;
void Equal<T>(T expected, T actual, string scenario)
{
    assertions++;
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException("Controller text editing check failed: " + scenario);
}
string Apply(string original, AuthGamepadTextEdit? edit) => edit is { } value
    ? original.Remove(value.Start, value.Length).Insert(value.Start, value.Replacement)
    : original;

Equal("aXbc", Apply("abc", AuthGamepadTextEditing.Insert("abc", 1, 0, "X")), "insert at caret");
Equal("aXYd", Apply("abcd", AuthGamepadTextEditing.Insert("abcd", 1, 2, "XY")), "replace selection");
Equal("aQd", Apply("abcd", AuthGamepadTextEditing.Insert("abcd", 1, 2, "Q", 4)), "replacement respects remaining capacity");
Equal("abcd", Apply("abcd", AuthGamepadTextEditing.Insert("abcd", 4, 0, "Q", 4)), "reject full field insertion");
Equal("abcdef", Apply("abcdef", AuthGamepadTextEditing.Insert("abcdef", 1, 1, "Q", 4)), "reject insertion without deleting selection when over limit");
Equal("ab", Apply("", AuthGamepadTextEditing.Insert("", 0, 0, "abcd", 2)), "truncate at valid boundary");
Equal("", Apply("", AuthGamepadTextEditing.Insert("", 0, 0, "😀", 1)), "do not split surrogate at maximum");
Equal("", Apply("", AuthGamepadTextEditing.Insert("", 0, 0, "e\u0301", 1)), "do not split combining sequence at maximum");
Equal("😀", Apply("", AuthGamepadTextEditing.Insert("", 0, 0, "😀", 2)), "allow complete surrogate pair");
Equal("اب", Apply("ا", AuthGamepadTextEditing.Insert("ا", 1, 0, "ب")), "Persian insertion");
Equal("a bc", Apply("abc", AuthGamepadTextEditing.Insert("abc", 1, 0, " ")), "space at caret");
Equal("Zabc", Apply("abc", AuthGamepadTextEditing.Insert("abc", -2, -1, "Z")), "clamp stale negative selection");
Equal("abcZ", Apply("abc", AuthGamepadTextEditing.Insert("abc", 99, 99, "Z")), "clamp stale oversized selection");
Equal("Zb", Apply("😀b", AuthGamepadTextEditing.Insert("😀b", 1, 1, "Z")), "selection never splits surrogate");
Equal("abc", Apply("abc", AuthGamepadTextEditing.Insert("abc", 1, 2, "")), "empty insertion does not erase selection");
Equal("ab", Apply("abc", AuthGamepadTextEditing.Backspace("abc", 3, 0)), "delete final character");
Equal("ac", Apply("abc", AuthGamepadTextEditing.Backspace("abc", 2, 0)), "delete before caret");
Equal("ad", Apply("abcd", AuthGamepadTextEditing.Backspace("abcd", 1, 2)), "delete selection");
Equal("", Apply("", AuthGamepadTextEditing.Backspace("", 0, 0)), "empty backspace");
Equal("abc", Apply("abc", AuthGamepadTextEditing.Backspace("abc", 0, 0)), "backspace at start");
Equal("a", Apply("a😀", AuthGamepadTextEditing.Backspace("a😀", 3, 0)), "delete complete surrogate pair");
Equal("a", Apply("ae\u0301", AuthGamepadTextEditing.Backspace("ae\u0301", 3, 0)), "delete complete combining sequence");
Equal("a", Apply("a👨‍👩‍👧", AuthGamepadTextEditing.Backspace("a👨‍👩‍👧", "a👨‍👩‍👧".Length, 0)), "delete full joined emoji");
Equal("", Apply("سلام", AuthGamepadTextEditing.Backspace("سلام", 0, 4)), "delete selected Persian text");
Equal(1, AuthGamepadTextEditing.MoveCaret("abcd", 1, 2, false), "previous collapses selection to start");
Equal(3, AuthGamepadTextEditing.MoveCaret("abcd", 1, 2, true), "next collapses selection to end");
Equal(3, AuthGamepadTextEditing.MoveCaret("a😀b", 1, 0, true), "next skips complete surrogate");
Equal(1, AuthGamepadTextEditing.MoveCaret("a😀b", 3, 0, false), "previous skips complete surrogate");
Equal(3, AuthGamepadTextEditing.MoveCaret("ae\u0301b", 1, 0, true), "next skips combining sequence");
Equal(1, AuthGamepadTextEditing.MoveCaret("ae\u0301b", 3, 0, false), "previous skips combining sequence");
Equal(0, AuthGamepadTextEditing.MoveCaret("abc", 0, 0, false), "previous stays at beginning");
Equal(3, AuthGamepadTextEditing.MoveCaret("abc", 3, 0, true), "next stays at end");
Equal(0, AuthGamepadTextEditing.MoveCaret("", 0, 0, true), "empty caret");
Equal(2, AuthGamepadTextEditing.Insert("abc", 1, 1, "Z")!.Value.Caret, "caret follows replacement");

// Only synthetic literals are used; neither actual contents nor mismatched values are printed.
Console.WriteLine($"Controller keyboard text-edit checks passed: {assertions} assertions.");
