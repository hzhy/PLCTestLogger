using System.Windows;
using PlcTestLogger.App.ViewModels;

namespace PlcTestLogger.App.Views;

public partial class SearchWindow : Window
{
    public SearchWindow(SearchViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
