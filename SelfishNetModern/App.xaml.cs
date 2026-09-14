using System.Windows;
using SelfishNetModern.Views;

namespace SelfishNetModern
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Prevent WPF from automatically shutting down when the modal dialog closes before MainWindow is created
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            if (!TermsOfUseDialog.HasAcceptedTerms())
            {
                var terms = new TermsOfUseDialog();
                bool? accepted = terms.ShowDialog();
                if (accepted != true)
                {
                    Shutdown();
                    return;
                }
            }

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
        }
    }
}
