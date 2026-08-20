using System;
using Avalonia.Controls;
using OpenUtauMobile.ViewModels;
using Serilog;

namespace OpenUtauMobile.Views;

public partial class MainWindow : Window
{
    private bool _closeConfirmed;
    private bool _closeRequestPending;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed)
        {
            return;
        }

        e.Cancel = true;
        if (_closeRequestPending)
        {
            return;
        }

        _closeRequestPending = true;
        try
        {
            if (DataContext is not MainViewModel mainViewModel ||
                !await mainViewModel.ConfirmCloseAsync())
            {
                return;
            }

            _closeConfirmed = true;
            Close();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error while confirming desktop window close");
        }
        finally
        {
            _closeRequestPending = false;
        }
    }
}
