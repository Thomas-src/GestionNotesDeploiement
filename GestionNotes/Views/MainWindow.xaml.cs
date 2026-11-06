
using System.Windows;
using GestionNotes.ViewModels;

namespace GestionNotes.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        DataContext = new MainViewModel();
    }
}
