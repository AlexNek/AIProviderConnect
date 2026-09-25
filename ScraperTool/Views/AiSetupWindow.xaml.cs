using System.Windows;

using ScraperTool.ViewModels;

namespace ScraperTool.Views;

public sealed partial class AiSetupWindow : Window
{
    public AiSetupWindow()
    {
        InitializeComponent();
    }

    private void OnSaveAndClose(object sender, RoutedEventArgs e)
    {
        if (DataContext is AiSetupViewModel vm)
        {
            vm.SaveCommand.Execute(null);
            DialogResult = true;
        }
    }
}
