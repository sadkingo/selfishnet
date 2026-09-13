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

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_StateChanged(object? sender, System.EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                MainContainer.Margin = new Thickness(8);
                if (MaximizeIcon != null) MaximizeIcon.Text = "❐";
                if (MaximizeBtn != null) MaximizeBtn.ToolTip = "Restore";
            }
            else
            {
                MainContainer.Margin = new Thickness(14);
                if (MaximizeIcon != null) MaximizeIcon.Text = "🗖";
                if (MaximizeBtn != null) MaximizeBtn.ToolTip = "Maximize";
            }
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