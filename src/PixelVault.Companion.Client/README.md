# PixelVault Companion client

This .NET 8 client supports owner sign-in/out, approved workspace identity/accounts, tracker listings/reads, guarded edits, history and undo through the browser's API. Native's explicit Companion window uses it; existing local tracker windows and automatic readers have not switched stores.

These are the ordinary/compact edit operations. Full native manual-override, source-review and authoring workflows need explicit server contracts before integration; do not silently replace those workflows with the guarded web check-off operation.

Supply a caller-owned `HttpClient` with a cookie-enabled handler, automatic redirects disabled, and the Companion HTTPS root as `BaseAddress`. The client handles antiforgery tokens after sign-in. It does not persist credentials, silently resolve conflicts or select arbitrary provider accounts. Keep the same `TrackerEdit.RequestId` for retries of an uncertain save; use a fresh ID only for a new or explicitly reviewed edit.

`CompanionRequestException.Status` distinguishes conflicts from authentication/validation errors. Read the latest document and ask the user to resolve a conflicting draft rather than silently resubmitting it with a newer version.

`CompanionEditor` owns one view's drafts and original field versions. Refreshes and saves of another field preserve those versions. A conflict must load the latest value before explicit rebasing; choosing the saved value removes only that draft. An unreadable/missing acknowledgement or server error retains the exact pending command. Editing is blocked until `RetryAsync` settles it, including history undo. The client pins requests to the authenticated workspace identity, so reconnecting to another prepared copy cannot reuse old drafts.

`CompanionConnection` creates an in-memory cookie handler with redirects disabled and no certificate bypass. HTTPS is the default for network destinations; loopback HTTP is allowed for the local preview, and LAN HTTP requires the explicit option. Credentials, cookies and drafts are never persisted. There is no local database dependency or offline write fallback in this client.
