# Header Back button state

The HomePage NavigationView back button stays visible and compiles a one-way
binding to its Frame.CanGoBack property. WinUI supplies the disabled state
when history is empty, including native keyboard/controller focus behavior.
Existing navigation and its MCP CanGoBack readback use that same Frame.

Owned path is Pages/HomePage.xaml. Verify the enabled and disabled appearance
interactively after entering and leaving a detail page.
