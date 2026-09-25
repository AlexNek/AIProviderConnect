using System.Windows;

using ScraperTool.ViewModels;

namespace ScraperTool.Views;

public sealed partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void OnSaveAndClose(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.SaveCommand.Execute(null);
            DialogResult = true;
        }
    }
}
