namespace PixelVaultNative.UI.Design
{
    /// <summary>
    /// Semantic color and chrome tokens for PixelVault UI (Slice A, <c>PV-PLN-V1POL-001</c>).
    /// Values match prior literals until an intentional visual refresh.
    /// </summary>
    public static class DesignTokens
    {
        /// <summary>App window / full-page background (dark shell).</summary>
        public const string PageBackground = "#0F1519";

        /// <summary>Raised panel behind forms (Path Settings inner margin grid).</summary>
        public const string PanelElevated = "#141B20";

        /// <summary>Default outline for panels, cards, and inputs.</summary>
        public const string BorderDefault = "#27313A";

        /// <summary>Field labels and secondary captions on medium-dark panels.</summary>
        public const string TextLabelMuted = "#A7B5BD";

        /// <summary>Text box / log well background.</summary>
        public const string InputBackground = "#0D1218";

        /// <summary>Primary text on inputs (Path Settings text boxes).</summary>
        public const string TextOnInput = "#E8EEF2";

        /// <summary>Primary action fill (Save, Path Settings entry, etc.).</summary>
        public const string ActionPrimaryFill = "#2B7A52";

        /// <summary>Secondary / quiet action fill (Cancel, toolbar chrome).</summary>
        public const string ActionSecondaryFill = "#20343A";

        /// <summary>Library toast panel background (ARGB).</summary>
        public const string ToastBackground = "#E6231E24";

        /// <summary>Library toast border.</summary>
        public const string ToastBorder = "#3A4A55";

        /// <summary>Toast body text.</summary>
        public const string TextToast = "#E8F0F5";

        /// <summary>Shortcut help: key column (muted).</summary>
        public const string TextShortcutMuted = "#8EA0AA";

        /// <summary>Shortcut help: description column.</summary>
        public const string TextShortcutBody = "#E4EDF2";

        /// <summary>Shortcut help: dismiss button (positive green).</summary>
        public const string ActionShortcutDismissFill = "#275D47";

        /// <summary>Corner radius for toast chrome (px).</summary>
        public const double RadiusToastDip = 10d;

        /// <summary>Health / status: check passed.</summary>
        public const string StatusOk = "#6BC98A";

        /// <summary>Health / status: attention recommended.</summary>
        public const string StatusWarn = "#D8B04A";

        /// <summary>Health / status: problem.</summary>
        public const string StatusBad = "#E07A7A";

        /// <summary>Health / status: informational (not a failure).</summary>
        public const string StatusNeutral = "#8EA0AA";
    }
}
