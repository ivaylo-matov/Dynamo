using System.Windows;
using Dynamo.Graph.Workspaces.Locking;

namespace Dynamo.UI.Prompts
{
    internal partial class GraphLockConflictDialog : Window
    {
        internal GraphLockConflictDialog(string title, string body, string readOnlyText, string openAnywayText, string cancelText)
        {
            InitializeComponent();

            Title = title;
            TitleText.Text = title;
            BodyText.Text = body;
            ReadOnlyButton.Content = readOnlyText;
            OpenAnywayButton.Content = openAnywayText;
            CancelButton.Content = cancelText;
            Decision = GraphLockUserDecision.ReadOnly;
        }

        internal GraphLockUserDecision Decision { get; private set; }

        private void ReadOnlyButton_Click(object sender, RoutedEventArgs e)
        {
            Decision = GraphLockUserDecision.ReadOnly;
            DialogResult = true;
        }

        private void OpenAnywayButton_Click(object sender, RoutedEventArgs e)
        {
            Decision = GraphLockUserDecision.Takeover;
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Decision = GraphLockUserDecision.Cancel;
            DialogResult = false;
        }
    }
}
