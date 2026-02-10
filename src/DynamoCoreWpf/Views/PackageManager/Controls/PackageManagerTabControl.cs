using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Dynamo.PackageManager.UI
{
    public class PackageManagerTabControl : TabControl
    {
        internal bool SuppressHomeEndNavigation { get; set; }
        internal UIElement SuppressHomeEndNavigationFocusScope { get; set; }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (SuppressHomeEndNavigation &&
                (e.Key == Key.Home || e.Key == Key.End) &&
                SuppressHomeEndNavigationFocusScope?.IsKeyboardFocusWithin == true)
            {
                // Skip base handling to prevent tab switching only when focus
                // is inside the publish wizard, but keep the event unhandled
                // so WebView2 can still process Home/End in text fields.
                return;
            }

            base.OnKeyDown(e);
        }
    }
}
