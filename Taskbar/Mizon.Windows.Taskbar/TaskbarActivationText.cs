namespace Mizon.Windows.Taskbar;

internal static class TaskbarActivationText
{
    internal static string? Status(string? languageTag, bool busy, TaskbarActivationOutcome? outcome)
    {
        var english = languageTag?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true;
        if (busy) return english ? "Opening Mizon…" : "در حال باز کردن میزون…";
        if (outcome is null || outcome.Success || outcome.Code is "canceled" or "disposed") return null;
        return outcome.Code switch
        {
            "timeout" => english ? "Mizon did not respond. Please try again." : "میزون پاسخ نداد؛ دوباره تلاش کنید.",
            "app_unavailable" or "protocol_unavailable" => english ? "Mizon is unavailable. Open the app and try again." : "میزون در دسترس نیست؛ برنامه را باز کنید و دوباره تلاش کنید.",
            "authentication_required" or "not_authenticated" => english ? "Sign in to Mizon to open this profile." : "برای باز کردن پروفایل وارد میزون شوید.",
            "not_found" or "content_not_found" => english ? "This profile could not be found." : "این پروفایل پیدا نشد.",
            _ => english ? "Could not open Mizon. Please try again." : "باز کردن میزون انجام نشد؛ دوباره تلاش کنید."
        };
    }
}
