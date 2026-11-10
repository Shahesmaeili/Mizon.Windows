using System.Runtime.InteropServices;

namespace Mizon.Windows.Taskbar;

internal sealed record TaskbarLayout(nint Window, Native.Rect Bounds, IReadOnlyList<TaskbarLayoutItem> Items);
internal sealed record TaskbarLayoutItem(string AutomationId, string ClassName, int ControlType, int ProcessId, bool Focusable, Native.Rect Bounds);

// Native UI Automation client. Cached properties are fetched in one bounded cross-process query,
// on a worker thread; no WPF, runtime COM wrappers, or Explorer UI mutations are involved.
internal static unsafe partial class TaskbarLayoutLocator
{
    internal static TaskbarLayout Read(nint taskbar)
    {
        Marshal.ThrowExceptionForHR(CoInitializeEx(0, 0)); // MTA worker; balanced even for S_FALSE.
        nint automation = 0, root = 0, cache = 0, condition = 0, array = 0;
        try
        {
            var clsid = new Guid("e22ad333-b25f-460c-83d0-0581107395c9"); // CUIAutomation8
            var iid = new Guid("34723aff-0c9d-49d0-9896-7ab52df8cd8a"); // IUIAutomation2
            Check(CoCreateInstance(in clsid, 0, 1, in iid, out automation));
            Check(((delegate* unmanaged[Stdcall]<nint, uint, int>)Slot(automation, 61))(automation, 300));
            Check(((delegate* unmanaged[Stdcall]<nint, uint, int>)Slot(automation, 63))(automation, 900));
            Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Slot(automation, 6))(automation, taskbar, &root));
            Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(automation, 20))(automation, &cache));
            Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(automation, 21))(automation, &condition));
            foreach (var property in new[] { 30001, 30002, 30003, 30009, 30011, 30012, 30022 })
                Check(((delegate* unmanaged[Stdcall]<nint, int, int>)Slot(cache, 3))(cache, property));
            Check(((delegate* unmanaged[Stdcall]<nint, int, int>)Slot(cache, 7))(cache, 1)); // TreeScope_Element
            Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(cache, 9))(cache, condition)); // raw tree
            Check(((delegate* unmanaged[Stdcall]<nint, int, int>)Slot(cache, 11))(cache, 0)); // AutomationElementMode_None
            Check(((delegate* unmanaged[Stdcall]<nint, int, nint, nint, nint*, int>)Slot(root, 8))(root, 4, condition, cache, &array));
            if (!Native.GetWindowRect(taskbar, out var bounds)) throw new InvalidOperationException("The taskbar moved or closed during its layout query.");
            var length = 0;
            if (array != 0) Check(((delegate* unmanaged[Stdcall]<nint, int*, int>)Slot(array, 3))(array, &length));
            if (length > 1024) throw new InvalidDataException("The taskbar automation tree exceeds the bounded layout query.");
            var items = new List<TaskbarLayoutItem>(length);
            for (var index = 0; index < length; index++)
            {
                nint element = 0;
                try
                {
                    Check(((delegate* unmanaged[Stdcall]<nint, int, nint*, int>)Slot(array, 4))(array, index, &element));
                    if (Integer(element, 70) != 0) continue; // cached IsOffscreen
                    var rectangle = default(Native.Rect);
                    Check(((delegate* unmanaged[Stdcall]<nint, Native.Rect*, int>)Slot(element, 75))(element, &rectangle));
                    if (rectangle.Right <= rectangle.Left || rectangle.Bottom <= rectangle.Top) continue;
                    items.Add(new(Text(element, 61), Text(element, 62), Integer(element, 53), Integer(element, 52), Integer(element, 59) != 0, rectangle));
                }
                finally { Release(element); }
            }
            // Other taskbar extensions need not expose UI Automation at all. Their real,
            // visible direct child HWND still occupies space and must never be covered.
            Native.GetWindowThreadProcessId(taskbar, out var shellProcess);
            var visited = new HashSet<nint>();
            for (var child = Native.GetWindow(taskbar, 5); child != 0; child = Native.GetWindow(child, 2))
            {
                if (visited.Count >= 512 || !visited.Add(child)) throw new InvalidOperationException("The taskbar child list changed during its bounded layout query.");
                Native.GetWindowThreadProcessId(child, out var process);
                if (process == shellProcess || process == Environment.ProcessId || !Native.IsWindowVisible(child)) continue;
                if (!Native.GetWindowRect(child, out var rectangle)) continue;
                items.Add(new("ForeignNativeChild", "Native.TaskbarChild", 50032, (int)process, true, rectangle));
            }
            return new(taskbar, bounds, items);
        }
        finally
        {
            Release(array); Release(condition); Release(cache); Release(root); Release(automation);
            CoUninitialize();
        }
    }

    internal static bool IsOccupied(TaskbarLayoutItem item, int ownProcessId)
    {
        if (item.ProcessId == ownProcessId) return false;
        if (item.Focusable) return true;
        if (item.ControlType is 50000 or 50002 or 50003 or 50005 or 50007 or 50011 or 50013 or 50015 or 50019 or 50027 or 50031) return true;
        return item.ClassName.StartsWith("SystemTray.", StringComparison.Ordinal)
            || item.ClassName.Contains("TaskListButton", StringComparison.Ordinal)
            || item.ClassName.Contains("Widgets", StringComparison.Ordinal);
    }

    internal static bool TryPlace(TaskbarLayout layout, int ownProcessId, int width, int height, int padding, out Native.Rect placement, Native.Rect? previous = null)
    {
        placement = default;
        var root = layout.Bounds;
        if (width <= 0 || height <= 0 || padding < 0 || width > root.Right - root.Left || height > root.Bottom - root.Top) return false;
        var horizontal = root.Right - root.Left >= root.Bottom - root.Top;
        var size = horizontal ? width : height;
        var start = horizontal ? root.Left : root.Top;
        var end = horizontal ? root.Right : root.Bottom;
        var occupied = new List<(int Start, int End)>();
        foreach (var item in layout.Items)
        {
            if (!IsOccupied(item, ownProcessId)) continue;
            var rect = item.Bounds;
            if (rect.Right <= root.Left || rect.Left >= root.Right || rect.Bottom <= root.Top || rect.Top >= root.Bottom) continue;
            var left = Math.Max(start, horizontal ? rect.Left : rect.Top);
            var right = Math.Min(end, horizontal ? rect.Right : rect.Bottom);
            if (right > left) occupied.Add((left, right));
        }
        // No positive evidence about the shell layout is safer than a guessed placement.
        if (occupied.Count == 0) return false;
        occupied.Sort(static (first, second) => first.Start.CompareTo(second.Start));
        if (previous is { } retained)
        {
            var retainedStart = horizontal ? retained.Left : retained.Top;
            var retainedEnd = horizontal ? retained.Right : retained.Bottom;
            var retainedCrossStart = horizontal ? retained.Top : retained.Left;
            var expectedCrossStart = horizontal ? root.Top + (root.Bottom - root.Top - height) / 2 : root.Left + (root.Right - root.Left - width) / 2;
            if (retained.Right - retained.Left == width && retained.Bottom - retained.Top == height && retainedCrossStart == expectedCrossStart
                && retainedStart >= start + padding && retainedEnd <= end - padding
                && !occupied.Any(block => retainedStart < block.End + padding && retainedEnd > block.Start - padding))
            {
                placement = retained;
                return true;
            }
        }
        var cursor = start;
        (int Start, int End)? preferred = null;
        foreach (var block in occupied)
        {
            if (block.Start - cursor >= size + padding * 2) preferred = (cursor, block.Start);
            cursor = Math.Max(cursor, block.End);
        }
        if (end - cursor >= size + padding * 2) preferred = (cursor, end);
        if (preferred is not { } gap) return false;
        var primary = gap.End - padding - size;
        var x = horizontal ? primary : root.Left + (root.Right - root.Left - width) / 2;
        var y = horizontal ? root.Top + (root.Bottom - root.Top - height) / 2 : primary;
        placement = new Native.Rect { Left = x, Top = y, Right = x + width, Bottom = y + height };
        return true;
    }

    private static int Integer(nint value, int slot)
    {
        var result = 0;
        Check(((delegate* unmanaged[Stdcall]<nint, int*, int>)Slot(value, slot))(value, &result));
        return result;
    }
    private static string Text(nint value, int slot)
    {
        nint text = 0;
        try
        {
            Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(value, slot))(value, &text));
            return text == 0 ? string.Empty : Marshal.PtrToStringBSTR(text);
        }
        finally { if (text != 0) Marshal.FreeBSTR(text); }
    }
    private static nint Slot(nint value, int slot) => (*(nint**)value)[slot];
    private static void Release(nint value) { if (value != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(value, 2))(value); }
    private static void Check(int result) => Marshal.ThrowExceptionForHR(result);
    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint flags);
    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();
    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint value);
}
