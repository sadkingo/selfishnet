using System.ComponentModel;
using System.Windows;
using SelfishNetModern.ViewModels;

namespace SelfishNetModern
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.OnClosing();
            }
            base.OnClosing(e);
        }
    }
}