using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Common;
using Mizon.Windows.Controls;
using Mizon.Windows.Mcp.Contracts;
using Mizon.Windows.Services;
using Mizon.Windows.Services.Gamepad;
using Mizon.Windows.UserControls;

internal static class Program
{
    private static int _checks;
    private static async Task Main()
    {
        await FocusAndKeyboard();
        await HelpScope();
        await OtherSurfaces();
        await FeedbackPreference();
        await Lifecycle();
        Console.WriteLine($"PASS {_checks} source-linked production navigator assertions. Synthetic visual tree only; no Windows UI or controller.");
    }
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        _checks++; Console.WriteLine("PASS " + description);
    }

    private static async Task FocusAndKeyboard()
    {
        using var page = new Fixture();
        page.First.Focus(FocusState.Keyboard);
        Check(await page.Apply(McpAuthGamepadAction.Next) && FocusManager.Current == page.Last,
            "Next skips a collapsed ancestor, disabled input and TextBox template buttons");
        Check(await page.Apply(McpAuthGamepadAction.Previous) && FocusManager.Current == page.First,
            "Previous follows only visible enabled page controls");
        page.Submit.Focus(FocusState.Keyboard);
        page.Session.Emit(AuthGamepadAction.PreviousField);
        Check(FocusManager.Current == page.Last, "Previous field from a non-field starts at the last field");
        page.First.Focus(FocusState.Keyboard);
        Check(await page.Apply(McpAuthGamepadAction.OpenKeyboard), "MCP opens the page keyboard only for an eligible field");
        Check(await page.Apply(McpAuthGamepadAction.Next) && page.Keyboard.Children.Contains((UIElement)FocusManager.Current!),
            "MCP focus moves inside an open keyboard without escaping to form controls");
        Check(page.Navigator.GetState() is { IsKeyboardVisible: true, FocusedFieldId: "identifier" }
            && page.Keyboard.HandledActions == 0 && page.Submit.Invocations == 0 && page.PageActions == 0,
            "Keyboard state identifies its editing field while MCP never activates a key or submits");
        Check(await page.Apply(McpAuthGamepadAction.CloseKeyboard) && !page.Navigator.GetState().IsKeyboardVisible,
            "Keyboard close restores page state without form submission");
        await page.Apply(McpAuthGamepadAction.OpenKeyboard);
        await page.Apply(McpAuthGamepadAction.Next);
        await page.Apply(McpAuthGamepadAction.ShowHelp);
        await page.Apply(McpAuthGamepadAction.HideHelp);
        Check(FocusManager.Current == page.First, "Help opened from keyboard restores the editing field after both overlays close");
        await page.Apply(McpAuthGamepadAction.OpenKeyboard);
        page.Last.Focus(FocusState.Keyboard);
        Check(!page.Keyboard.IsOpen && page.Keyboard.EditingTarget is null && FocusManager.Current == page.Last,
            "Focusing another form field closes the keyboard before it can edit a stale target");
        page.First.Focus(FocusState.Keyboard);
        page.First.Visibility = Visibility.Collapsed;
        Check(!await page.Apply(McpAuthGamepadAction.OpenKeyboard), "Stale focus on a collapsed field cannot reopen its keyboard");
        page.First.Visibility = Visibility.Visible;
        var outsideField = new TextBox(); page.Root.Children.Add(outsideField); outsideField.Focus(FocusState.Keyboard);
        Check(!await page.Apply(McpAuthGamepadAction.OpenKeyboard), "Unlisted input inside the page is not eligible for the auth keyboard");
    }

    private static async Task HelpScope()
    {
        using var page = new Fixture();
        page.First.Focus(FocusState.Keyboard);
        await page.Apply(McpAuthGamepadAction.ShowHelp);
        var helpFocus = FocusManager.Current;
        Check(helpFocus is CheckBox && page.Navigator.GetState().IsHelpVisible, "Opening help moves focus into its own controls");
        await page.Apply(McpAuthGamepadAction.ShowHelp);
        await page.Apply(McpAuthGamepadAction.HideHelp);
        Check(FocusManager.Current == page.First, "Repeated ShowHelp preserves the original return focus");
        page.Last.Focus(FocusState.Keyboard);
        await page.Apply(McpAuthGamepadAction.HideHelp);
        Check(FocusManager.Current == page.Last, "Repeated HideHelp does not replay a stale focus restore");
        await page.Apply(McpAuthGamepadAction.ShowHelp);
        page.Submit.Focus(FocusState.Keyboard); // Pointer focus can leave a visual overlay.
        page.Session.Emit(AuthGamepadAction.Accept);
        Check(page.Submit.Invocations == 0 && FocusManager.Current is CheckBox,
            "Accept while help is visible cannot activate a form button behind the overlay");
        page.Session.Emit(AuthGamepadAction.NextField);
        page.Session.Emit(AuthGamepadAction.Submit);
        Check(FocusManager.Current is CheckBox && page.PageActions == 0, "Help traps field navigation and blocks physical submission");
        page.First.Focus(FocusState.Keyboard);
        Check(!await page.Apply(McpAuthGamepadAction.OpenKeyboard), "Help visibility blocks keyboard opening after pointer focus escapes");
    }

    private static async Task OtherSurfaces()
    {
        using var page = new Fixture();
        var external = new AuthActionButton(); external.Focus(FocusState.Keyboard);
        page.Session.Emit(AuthGamepadAction.Accept); page.Session.Emit(AuthGamepadAction.Submit);
        Check(external.Invocations == 0 && page.PageActions == 0 && !await page.Apply(McpAuthGamepadAction.Next),
            "Header or another page's focus cannot be activated, submitted through or stolen");
        page.First.Focus(FocusState.Keyboard);
        VisualTreeHelper.OpenPopups.Add(new Popup { Child = new ContentDialog() });
        page.Session.Emit(AuthGamepadAction.Submit);
        Check(page.PageActions == 0 && !await page.Apply(McpAuthGamepadAction.Next),
            "An unrelated modal blocks input even while an auth field retains focus");
        FocusManager.Current = null;
        Check(!await page.Apply(McpAuthGamepadAction.ShowHelp), "A modal with no focused element cannot be displaced by auth help");
        VisualTreeHelper.OpenPopups.Clear();
        var combo = new ComboBox(); page.Root.Children.Add(combo); combo.Focus(FocusState.Keyboard);
        page.Session.Emit(AuthGamepadAction.Accept);
        Check(combo.IsDropDownOpen, "Physical activation opens the page-owned selector");
        var selection = combo.SelectedIndex;
        Check(!await page.Apply((McpAuthGamepadAction)999) && combo.IsDropDownOpen,
            "Undefined MCP enum has no focus or popup side effects");
        var comboItem = new ComboBoxItem { Owner = combo };
        VisualTreeHelper.OpenPopups.Add(new Popup { Child = comboItem });
        comboItem.Focus(FocusState.Keyboard);
        Check(await page.Apply(McpAuthGamepadAction.Next) && !combo.IsDropDownOpen && combo.SelectedIndex == selection,
            "MCP navigation recognizes the owned selector popup and closes it without changing selection or activating controls");
        VisualTreeHelper.OpenPopups.Clear();
        var dialog = new ContentDialog(); var dialogButton = new ButtonBase();
        dialog.Nodes.Add(dialogButton); dialogButton.Parent = dialog; page.Root.Children.Add(dialog); dialogButton.Focus(FocusState.Keyboard);
        page.Session.Emit(AuthGamepadAction.Accept); page.Session.Emit(AuthGamepadAction.Submit);
        Check(dialogButton.Invocations == 0 && page.PageActions == 0 && !await page.Apply(McpAuthGamepadAction.ShowHelp),
            "A dialog nested in the page still owns input and cannot be operated by auth tools");
    }

    private static async Task Lifecycle()
    {
        var page = new Fixture();
        page.First.Focus(FocusState.Keyboard);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { await page.Navigator.ApplyAsync(McpAuthGamepadAction.ShowHelp, cancellation.Token); throw new InvalidOperationException("Expected cancellation."); }
        catch (OperationCanceledException) { Check(!page.Navigator.GetState().IsHelpVisible, "Pre-cancelled MCP input makes no UI state change"); }
        await page.Apply(McpAuthGamepadAction.OpenKeyboard);
        page.Navigator.SetActive(false);
        Check(!page.Navigator.GetState().IsKeyboardVisible && !await page.Apply(McpAuthGamepadAction.Next),
            "Deactivation dismisses keyboard and rejects subsequent actions");
        page.Navigator.SetActive(true); page.First.Focus(FocusState.Keyboard);
        await page.Apply(McpAuthGamepadAction.ShowHelp);
        page.Session.Disconnect();
        Check(!page.Navigator.GetState().IsHelpVisible, "Controller disconnect dismisses the scoped help surface");
        page.Dispose(); page.Dispose();
        Check(page.Session.Disposed && page.Root.InputSubscribers == 0 && page.Root.LayoutSubscribers == 0
            && LocalizationService.Current.Subscribers == 0 && page.Root.Children.Count == page.InitialChildren
            && page.Scroller.Padding == new Thickness(3), "Dispose releases subscriptions, overlay, controller ownership and scroller reservation");
        Check(!await page.Apply(McpAuthGamepadAction.Next), "Disposed navigator cannot mutate page focus");
    }

    private static async Task FeedbackPreference()
    {
        using var page = new Fixture();
        page.First.Focus(FocusState.Keyboard);
        Check(!page.Navigator.GetState().IsFeedbackEnabled && !page.Session.FeedbackEnabled,
            "Controller feedback starts disabled in each page session");
        await page.Apply(McpAuthGamepadAction.EnableFeedback);
        await page.Apply(McpAuthGamepadAction.EnableFeedback);
        Check(page.Navigator.GetState().IsFeedbackEnabled && page.Session.FeedbackEnabled
            && FocusManager.Current == page.First && page.PageActions == 0 && page.Submit.Invocations == 0,
            "Repeated feedback enable sets the shared session preference without focus changes or activation");
        await page.Apply(McpAuthGamepadAction.DisableFeedback);
        Check(!page.Navigator.GetState().IsFeedbackEnabled && !page.Session.FeedbackEnabled,
            "Explicit feedback disable is reflected by the same production state snapshot");
    }

    private sealed class Fixture : IDisposable
    {
        public Grid Root { get; } = new();
        public ScrollViewer Scroller { get; } = new() { Padding = new Thickness(3) };
        public TextBox First { get; } = new() { Name = "identifier" };
        public PasswordBox Last { get; } = new() { Name = "password", Top = 80 };
        public AuthActionButton Submit { get; } = new() { Top = 140 };
        public int PageActions { get; private set; }
        public int InitialChildren { get; }
        public AuthGamepadNavigation Navigator { get; }
        public AuthGamepadSession Session { get; }
        public AuthGamepadKeyboard Keyboard { get; }
        public Fixture()
        {
            FocusManager.Current = null; VisualTreeHelper.OpenPopups.Clear();
            Root.Children.Add(First);
            var templateButton = new ButtonBase(); First.Nodes.Add(templateButton); templateButton.Parent = First;
            var hidden = new Grid { Visibility = Visibility.Collapsed }; hidden.Children.Add(new TextBox()); Root.Children.Add(hidden);
            Root.Children.Add(new TextBox { IsEnabled = false }); Root.Children.Add(Last); Root.Children.Add(Submit);
            InitialChildren = Root.Children.Count;
            Navigator = new(Root, Scroller, _ => { PageActions++; return Task.CompletedTask; }, () => [First, Last], control => control == First ? "identifier" : control == Last ? "password" : null, () => { });
            Session = AuthGamepadSession.Latest; Keyboard = AuthGamepadKeyboard.Latest;
            Navigator.SetActive(true);
        }
        public Task<bool> Apply(McpAuthGamepadAction action) => Navigator.ApplyAsync(action, CancellationToken.None);
        public void Dispose() { Navigator.Dispose(); VisualTreeHelper.OpenPopups.Clear(); FocusManager.Current = null; }
    }
}
