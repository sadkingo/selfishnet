using System;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace SelfishNetModern.Views
{
    public partial class TermsOfUseDialog : Window
    {
        private static readonly string AcceptanceFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SelfishNetModern",
            "accepted_terms.txt"
        );

        public TermsOfUseDialog()
        {
            InitializeComponent();
            MouseDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };
        }

        public static bool HasAcceptedTerms()
        {
            try
            {
                if (File.Exists(AcceptanceFilePath))
                {
                    string content = File.ReadAllText(AcceptanceFilePath).Trim();
                    return content.Equals("ACCEPTED", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { }
            return false;
        }

        private void OnCheckBoxChanged(object sender, RoutedEventArgs e)
        {
            AcceptButton.IsEnabled = AgreementCheckBox.IsChecked == true;
        }

        private void OnAcceptClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                var dir = Path.GetDirectoryName(AcceptanceFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(AcceptanceFilePath, "ACCEPTED");
            }
            catch { }

            DialogResult = true;
            Close();
        }

        private void OnDeclineClicked(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
