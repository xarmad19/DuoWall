using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace DuoWall.App.Windows;

public partial class AboutMeDialogWindow : Window
{
    public AboutMeDialogWindow()
    {
        InitializeComponent();
        MouseDown += (s, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        };
    }

    private void SendFeedback_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "mailto:engr.sarmad.iftikhar@gmail.com?subject=DuoWall%20Feedback%20%26%20Feature%20Request",
                UseShellExecute = true
            });
        }
        catch
        {
            // Fallback: clipboard or ignore
        }
    }

    private void OpenGitHub_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/xarmad19/DuoWall",
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
