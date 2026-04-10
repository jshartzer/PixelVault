using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace PixelVaultNative
{
    /// <summary>Small UI affordances for keyboard focus and assistive tech (library-wide).</summary>
    internal static class AccessibilityUi
    {
        internal static void TryApplyFocusVisualStyle(Control control)
        {
            if (control == null) return;
            var app = Application.Current;
            if (app == null) return;
            if (app.TryFindResource("PixelVaultFocusVisual") is Style style)
                control.FocusVisualStyle = style;
        }

        internal static void TrySetAutomationName(FrameworkElement element, string name)
        {
            if (element == null || string.IsNullOrWhiteSpace(name)) return;
            AutomationProperties.SetName(element, name.Trim());
        }
    }
}
