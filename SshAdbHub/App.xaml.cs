using System.Windows;
using System.Windows.Threading;

namespace SshAdbHub;

public partial class App : Application
{
    private void App_Unhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.ToString(), "Unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
