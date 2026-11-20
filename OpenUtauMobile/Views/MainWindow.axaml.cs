using System;
using Avalonia.Controls;
using OpenUtauMobile.ViewModels;
using OpenUtauMobile.Services.Projects;
using OpenUtau.Core;
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

        if (DataContext is not MainViewModel mainViewModel)
        {
            // No VM to confirm against; allow the close to proceed.
            _closeConfirmed = true;
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
            var project = DocManager.Inst.Project;
            var revision = DocManager.Inst.Revision;
            if (!await mainViewModel.ConfirmCloseAsync())
            {
                return;
            }

            await PreferencesWriter.Inst.FlushAsync();
            if (!ReferenceEquals(project, DocManager.Inst.Project) || revision != DocManager.Inst.Revision) return;
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
