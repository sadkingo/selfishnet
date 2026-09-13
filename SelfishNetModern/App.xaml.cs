using System.Windows;
using SelfishNetModern.Views;

namespace SelfishNetModern
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

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
        }
    }
}
