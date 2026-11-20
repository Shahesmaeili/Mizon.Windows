using System;
using System.Globalization;

namespace Mizon.Windows.Services;

internal enum TaskbarOnlinePhase { Disconnected, Connecting, Reconnecting, NoInternet, Connected }

internal readonly record struct TaskbarOnlineSnapshot(
    TaskbarOnlinePhase Phase, int? OnlineCount, int? OnlineFollowerCount, bool Animate)
{
    internal string Encode(nint window, uint processId) => FormattableString.Invariant(
        $"{window}|{processId}|{(int)Phase}|{OnlineCount}|{OnlineFollowerCount}|{(Animate ? 1 : 0)}");

    internal static TaskbarOnlineSnapshot? Decode(string text, nint window, uint processId)
    {
        var fields = text.Split('|');
        if (fields.Length != 6 ||
            !long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var handle) || handle != window ||
            !uint.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var process) || process != processId ||
            !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var phase) ||
            phase is < (int)TaskbarOnlinePhase.Disconnected or > (int)TaskbarOnlinePhase.Connected ||
            !TryCount(fields[3], out var online) || !TryCount(fields[4], out var followers) ||
            fields[5] is not ("0" or "1")) return null;

        var connected = (TaskbarOnlinePhase)phase == TaskbarOnlinePhase.Connected;
        return new((TaskbarOnlinePhase)phase, connected ? online : null, connected ? followers : null, fields[5] == "1");

        static bool TryCount(string field, out int? count)
        {
            count = null;
            if (field.Length == 0) return true;
            if (!int.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)) return false;
            count = parsed;
            return true;
        }
    }
}
