# Navigation invocation results

`mizon_open_destination` continues using the existing independent navigation
control permission and allowlisted destinations. It does not gain playback,
download, authentication submission or arbitrary URI execution permission.

When a route returns an outcome, the response can contain `navigationResult` if
the connection also has **navigation read** permission. Control-only callers
still receive the normal `success`, `code` and sanitized `message` fields, with no
navigation snapshot or additional result data. Other read scopes do not grant
access to these fields.

The receipt contains version `1`, a fresh `requestId` UUID, `success`, `code`,
`message` and optional `data`. The bounded data fields are:

- `destination` and its canonical `mizon://` URI;
- `publicId` and optional `commentPublicId`;
- optional `settingsSection`;
- `completionStage`: `opened` or `completed`.

`opened` means the destination was opened; content can still be loading.
`completed` means that route's declared operation completed. The adapter preserves
the actual handler's stage and does not infer completion from a visible page.
Failure may omit data entirely. A timeout, cancelled/revoked request or disposed
adapter does not manufacture a successful receipt. `mizon_get_navigation` reads
the current location and does not retain/replay earlier invocation receipts.

The existing `navigation.pendingUri` also reflects an in-flight native
protocol-for-results request. It is cleared when that response finishes, times
out or is replaced, even if an older asynchronous operation is still unwinding.
It stays behind navigation read permission and does not expose the companion's
private UI, credentials or additional business data.

The app/taskbar result transport shares `Services/AppDeepLinkResult.cs`. Its
source-generated camel-case JSON rejects unknown/duplicate properties, missing
required fields, mismatched/empty request UUIDs, other protocol versions and
responses over 16 KiB UTF-8. The result keys are `mizon.response`,
`mizon.requestId` and `mizon.responseVersion`. This pure contract contains no WinRT
activation, callback endpoint, filesystem path or arbitrary object payload.
Native activation and sender validation belong to the application transport.

The producer is responsible for returning a sanitized human-readable message;
the shared validator additionally bounds it and rejects control characters.
Transport correlation is not authentication or a substitute for MCP consent.
