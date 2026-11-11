namespace Mizon.Windows.Taskbar;

// Opt-in deterministic layout checks. They never initialize UI Automation or alter the shell.
internal static class TaskbarLayoutChecks
{
    internal static int Run()
    {
        var count = 0;
        static Native.Rect Rect(int x, int y, int width, int height) => new() { Left = x, Top = y, Right = x + width, Bottom = y + height };
        static TaskbarLayoutItem Item(string name, Native.Rect bounds, int process = 1) => new(name, "Button", 50000, process, false, bounds);
        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("Layout check failed: " + description);
            count++;
        }
        var root = Rect(0, 1020, 1920, 60);
        TaskbarLayout layout = new(1, root,
        [Item("Widgets", Rect(8, 1020, 190, 60)), Item("Apps", Rect(555, 1020, 809, 60)),
         new("ForeignNativeChild", "Native.TaskbarChild", 50032, 2, true, Rect(1041, 1020, 525, 60)),
         Item("Tray", Rect(1566, 1020, 354, 60))]);
        Check(TaskbarLayoutLocator.TryPlace(layout, 99, 45, 45, 10, out var avatar), "real taskbar fixture has a gap");
        Check(avatar.Left == 500 && avatar.Right == 545 && avatar.Top == 1027 && avatar.Bottom == 1072, "avatar fits before Start and outside Halo");
        Check(TaskbarLayoutLocator.TryPlace(layout, 99, 300, 45, 10, out var wide) && wide.Right - wide.Left == 300, "arbitrary wide host fits using its whole width");
        Check(!TaskbarLayoutLocator.TryPlace(layout, 99, 340, 45, 10, out _), "wide host with insufficient clearance is hidden");
        Check(!TaskbarLayoutLocator.TryPlace(layout, 99, 45, 61, 10, out _), "host taller than the taskbar is hidden");
        Check(TaskbarLayoutLocator.TryPlace(layout, 99, 45, 45, 10, out var repeated) && repeated.Equals(avatar), "unchanged occupancy produces the same bounds without jitter");
        Check(TaskbarLayoutLocator.TryPlace(layout, 99, 100, 45, 10, out var resized) && resized.Right - resized.Left == 100, "resized host uses its complete current footprint");
        // The profile and mood are one 80-DIP host, including their eight-DIP gap.
        // An interval that fits only the old avatar must never clip the second action.
        Check(TaskbarLayoutLocator.TryPlace(layout, 99, 80, 36, 8, out var pair)
            && pair.Left == 467 && pair.Right == 547 && pair.Top == 1032 && pair.Bottom == 1068,
            "adding mood repositions the complete pair instead of retaining avatar-only bounds");
        var avatarOnlyGap = new TaskbarLayout(1, root,
            [Item("left", Rect(0, 1020, 400, 60)), Item("right", Rect(460, 1020, 1460, 60))]);
        Check(TaskbarLayoutLocator.TryPlace(avatarOnlyGap, 99, 36, 36, 8, out _)
            && !TaskbarLayoutLocator.TryPlace(avatarOnlyGap, 99, 80, 36, 8, out _),
            "avatar-only clearance hides the complete pair rather than overlapping another taskbar item");
        var pairBlocked = new TaskbarLayout(1, root, [.. layout.Items, Item("new app", Rect(511, 1020, 36, 60))]);
        Check(TaskbarLayoutLocator.TryPlace(pairBlocked, 99, 80, 36, 8, out var movedPair)
            && movedPair.Left == 423 && movedPair.Right == 503,
            "an item appearing under the pair moves the entire host left");
        Check(TaskbarLayoutLocator.TryPlace(layout, 99, 80, 36, 8, out var restoredPair)
            && restoredPair.Equals(pair), "removing the temporary item restores the original right-aligned position");
        for (var cycle = 0; cycle < 4; cycle++)
        {
            Check(TaskbarLayoutLocator.TryPlace(pairBlocked, 99, 80, 36, 8, out var blockedAgain) && blockedAgain.Equals(movedPair)
                && TaskbarLayoutLocator.TryPlace(layout, 99, 80, 36, 8, out var restoredAgain) && restoredAgain.Equals(pair),
                "repeated insertion and removal never accumulates leftward drift");
        }
        var reordered = layout with { Items = layout.Items.Reverse().Concat(layout.Items.Take(1)).ToArray() };
        Check(TaskbarLayoutLocator.TryPlace(reordered, 99, 80, 36, 8, out var samePair) && samePair.Equals(pair),
            "automation ordering and duplicate obstacles cannot move an unchanged layout");
        Check(!TaskbarLayoutLocator.TryPlace(new(1, root, []), 99, 45, 45, 10, out _), "missing occupancy evidence never guesses");
        Check(!TaskbarLayoutLocator.TryPlace(new(1, root, [Item("full", root)]), 99, 45, 45, 10, out _), "fully occupied taskbar hides host");
        var shifted = new TaskbarLayout(1, Rect(-1920, -60, 1920, 60),
            [Item("left", Rect(-1920, -60, 200, 60)), Item("right", Rect(-1300, -60, 1300, 60))]);
        Check(TaskbarLayoutLocator.TryPlace(shifted, 99, 200, 45, 10, out var negative) && negative.Left < 0 && negative.Top == -53, "negative monitor coordinates remain physical screen coordinates");
        var vertical = new TaskbarLayout(1, Rect(0, 0, 60, 1080),
            [Item("top", Rect(0, 0, 60, 200)), Item("bottom", Rect(0, 500, 60, 580))]);
        Check(TaskbarLayoutLocator.TryPlace(vertical, 99, 45, 100, 10, out var tall) && tall.Top == 390 && tall.Bottom == 490 && tall.Left == 7, "vertical taskbar uses host height for intervals");
        var scaled = new TaskbarLayout(1, Rect(0, 0, 2880, 90),
            [Item("left", Rect(0, 0, 300, 90)), Item("right", Rect(1200, 0, 1680, 90))]);
        Check(TaskbarLayoutLocator.TryPlace(scaled, 99, 54, 54, 12, out var scaledBounds) && scaledBounds.Right == 1188 && scaledBounds.Top == 18, "DPI-scaled footprint and clearance are honored");
        Check(TaskbarLayoutLocator.TryPlace(scaled, 99, 120, 54, 12, out var scaledPair)
            && scaledPair.Right == 1188 && scaledPair.Left == 1068 && scaledPair.Top == 18,
            "150-percent scaling reserves both 36-DIP buttons and their eight-DIP gap");
        void CheckReturnAfterRemoval(TaskbarLayout baseline, TaskbarLayoutItem temporary, int width, int height, int clearance)
        {
            var blocked = baseline with { Items = baseline.Items.Append(temporary).ToArray() };
            Check(TaskbarLayoutLocator.TryPlace(baseline, 99, width, height, clearance, out var before)
                && TaskbarLayoutLocator.TryPlace(blocked, 99, width, height, clearance, out var during)
                && !before.Equals(during)
                && TaskbarLayoutLocator.TryPlace(baseline, 99, width, height, clearance, out var after)
                && after.Equals(before), "temporary occupancy removal restores placement for " + temporary.AutomationId);
        }
        CheckReturnAfterRemoval(layout, Item("avatar only", Rect(511, 1020, 36, 60)), 36, 36, 8);
        CheckReturnAfterRemoval(scaled, Item("150-percent DPI", Rect(1120, 0, 80, 90)), 120, 54, 12);
        CheckReturnAfterRemoval(shifted, Item("negative coordinates", Rect(-1350, -60, 50, 60)), 200, 45, 10);
        CheckReturnAfterRemoval(vertical, Item("vertical taskbar", Rect(0, 450, 60, 50)), 45, 100, 10);
        var noSpace = new TaskbarLayout(1, root, new[] { Item("temporary full occupancy", root) });
        Check(!TaskbarLayoutLocator.TryPlace(noSpace, 99, 80, 36, 8, out _)
            && TaskbarLayoutLocator.TryPlace(layout, 99, 80, 36, 8, out var visibleAgain) && visibleAgain.Equals(pair),
            "host returns to its preferred position after all free space was temporarily occupied");
        var own = new TaskbarLayout(1, root, [.. layout.Items, Item("self", Rect(198, 1020, 357, 60), 99)]);
        Check(TaskbarLayoutLocator.TryPlace(own, 99, 45, 45, 10, out _), "own child is excluded from occupancy");
        foreach (var item in layout.Items)
            Check(avatar.Right + 10 <= item.Bounds.Left || avatar.Left - 10 >= item.Bounds.Right, "chosen host has required clearance from " + item.AutomationId);
        foreach (var item in layout.Items)
            Check(pair.Right + 8 <= item.Bounds.Left || pair.Left - 8 >= item.Bounds.Right, "complete pair has required clearance from " + item.AutomationId);
        return count;
    }
}
