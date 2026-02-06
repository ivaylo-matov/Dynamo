using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Dynamo.PackageManager.UI
{
    public class PackageManagerTabControl : TabControl
    {
        public bool SuppressHomeEndNavigation { get; set; }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (SuppressHomeEndNavigation && (e.Key == Key.Home || e.Key == Key.End))
            {
                // Skip base handling to prevent tab switching, but do not
                // mark as handled so WebView2 can still process the key.
                return;
            }

            base.OnKeyDown(e);
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.PageUp || e.Key == Key.PageDown)
            {
                if (!SuppressHomeEndNavigation && !IsWpfTextInputFocused())
                {
                    var direction = e.Key == Key.PageUp ? -1 : 1;
                    var newIndex = SelectedIndex + direction;

                    if (Items.Count > 0)
                    {
                        if (newIndex < 0)
                        {
                            newIndex = 0;
                        }
                        else if (newIndex >= Items.Count)
                        {
                            newIndex = Items.Count - 1;
                        }

                        if (newIndex != SelectedIndex)
                        {
                            SelectedIndex = newIndex;
                        }
                    }

                    e.Handled = true;
                    return;
                }
            }

            base.OnPreviewKeyDown(e);
        }

        private static bool IsWpfTextInputFocused()
        {
            var focusedElement = Keyboard.FocusedElement as System.Windows.DependencyObject;
            if (focusedElement == null)
            {
                return false;
            }

            return focusedElement is TextBoxBase || focusedElement is PasswordBox;
        }
    }
}
