using System;
using System.Collections.Generic;

namespace Mizon.Windows.Services;

internal enum AuthSubmissionOutcome { Success, Error, Validation }

// UI feedback follows a completed attempt, not error text/property changes.
// This event never carries credentials, a token or the backend response.
internal sealed class AuthSubmissionCompletedEventArgs(AuthSubmissionOutcome outcome) : EventArgs
{
    internal AuthSubmissionOutcome Outcome { get; } = outcome;
    internal Dictionary<string, object>? NavigationParameters { get; set; }
}
