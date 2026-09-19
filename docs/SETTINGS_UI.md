# Settings navigation and opening performance

September 7, 2026 · App Testing update (no release version bump).

Settings now follows HealthApp's top-tab layout: **Folders & tools**, **Accounts**, **Import & tray**, **Library**, **Diagnostics**, and **Run history**. Folders, credentials, and background preferences can be edited directly in Settings. Drafts survive tab switches; Save Settings applies these three configuration pages. Library and diagnostics switches continue to save immediately. The standalone configuration entry point uses the same first three pages.

The previous opening path called `LoadLogView` before `ShowDialog`. That read the entire main log and assigned it to a wrapping WPF TextBox. The local main log was 20,544,308 bytes during investigation. Its size, decoding, and text layout all contributed work before Settings could appear.

The new shell does not load history on opening. Library, Diagnostics, and Run history pages are created on first selection. History reads at most the last 64 KiB on a worker thread, uses unwrapped text, and refreshes when revisiting its tab or clicking Refresh history. Open logs folder provides access to the complete, unchanged file. Closing the window during a read prevents its completion from updating the closed UI. Optional troubleshooting logging records Settings first-render time.

Validation: 69 targeted settings tests passed, including a log larger than 20 MB, UTF-8 boundary handling, missing logs, draft preservation, save behavior, and WPF layout at normal/minimum sizes. An initial test measured construction plus first layout at 315 ms; this is a test fixture measurement, not a production end-to-end timing guarantee.

To regenerate WPF layout previews, set `PIXELVAULT_SETTINGS_PREVIEW` to an output directory and run `SettingsShellTests`. Previews use fixture values rather than real credentials.
