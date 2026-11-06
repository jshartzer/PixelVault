# Heritage completion covers

Approved November 1, 2026. Native 0.116.020 and Companion browser revision `0.43.1-heritage1` implement the refined Heritage mockup.

Gold, emerald and diamond share a slim beveled rail, shallow perimeter tracery, upper-right/lower-left thematic accents, and a lower-right completion clasp. Gold uses an engraved circular medallion without a wreath; emerald uses a faceted green lozenge; diamond uses an octagonal platinum setting. Decorations preserve the artwork and do not intercept input. Cabinet frames and completion classification are separate and unchanged.

`scripts/Build-HeritageCompletionAssets.py` is the geometry source. Run it with Python to regenerate the nine `heritage-*.svg` files under Companion's `wwwroot/completion-frames` and Native's `HeritageCompletionOrnaments.g.cs`. At a 220px cover width, corners occupy a 42px square but follow the edges, the rail is 4px, and the clasp is 56px with a 1px corner offset. Scale is bounded for small tiles and large profiles.

Native uses `MainWindow.BuildLibraryCompletionFrameDrawing` for all three tiers and retains one frozen drawing per size, DPI and high-contrast state. `HolofoilCover` enables the existing color-preserving shader and perspective only for `BaseAndDlcAndPv`; leaving diamond restores the prior artwork effect and stops projection. This applies to library, profile and completion-case covers. Earned Century achievement artwork continues to use its separate finish.

Companion uses `completion.css`, `completionDecoration` and the same generated SVG geometry. Only diamond markup contains a full-cover foil layer. Delegated pointer lighting supports newly rendered library and completion-case covers without intercepting navigation or storing state. Reduced motion retains a static foil; forced colors hides decorative layers and uses system-colored borders and clasp text. The browser service does not require a restart for these static asset changes.

Validation: 37 focused WPF tests, 19 library/showcase unit tests, the existing cover-controls browser test, and the new completion browser test. Browser coverage exercises navigation, 320/390/768/1050px layouts, corner placement, unchanged gold/emerald pixels, diamond light movement/reset, reduced motion, forced colors and a completion downgrade. Actual WPF cover renders verify only diamond projects, with identical restored/flat images. Evidence is in `artifacts/heritage-completion`; build/test logs are `tmp/heritage-*.log`.
