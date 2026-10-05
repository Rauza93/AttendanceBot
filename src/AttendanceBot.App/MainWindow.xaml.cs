using System.Windows;
using WpfButton = System.Windows.Controls.Button;
namespace AttendanceBot.App;
public partial class MainWindow : Window
{
    public MainWindow() { InitializeComponent(); }
    private void NavigateClick(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: string tag }) Pages.SelectedIndex = tag switch { "Dashboard" => 0, "Profiles" => 1, "Schedule" => 2, "History" => 3, "Settings" => 4, "Logs" => 5, _ => 0 };
    }
}
