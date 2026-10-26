using System;
using System.Runtime.InteropServices;

namespace Mizon.Windows.Common;

public static class KeyboardLayoutNativeIntrop
{
    [DllImport("user32.dll")]
    private static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint Flags);

    [DllImport("user32.dll")]
    private static extern IntPtr ActivateKeyboardLayout(IntPtr hkl, uint Flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    private const string ENGLISH_LAYOUT = "00000409"; // English (US)

    public static void SwitchToEnglish()
    {
        var hkl = LoadKeyboardLayout(ENGLISH_LAYOUT, 1);
        ActivateKeyboardLayout(hkl, 0);
    }

    public static string GetCurrentKeyboardLanguage()
    {
        IntPtr hkl = GetKeyboardLayout(0);

        int languageId = hkl.ToInt32() & 0xFFFF;

        var culture = new System.Globalization.CultureInfo(languageId);

        return culture.Name; // مثل en-US یا fa-IR
    }
}
