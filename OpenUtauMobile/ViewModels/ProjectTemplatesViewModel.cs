using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reactive;
using OpenUtau.Core;
using OpenUtauMobile.Services.Dialogs;
using ReactiveUI;

namespace OpenUtauMobile.ViewModels
{
    public sealed record ProjectTemplateItem(string Path)
    {
        public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
    }

    public sealed class ProjectTemplatesViewModel : PopupViewModelBase
    {
        public ObservableCollection<ProjectTemplateItem> Items { get; } = [];
        public ReactiveCommand<ProjectTemplateItem, Unit> OpenCommand { get; }
        public ReactiveCommand<ProjectTemplateItem, Unit> DeleteCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }

        public ProjectTemplatesViewModel()
        {
            Directory.CreateDirectory(PathManager.Inst.TemplatesPath);
            foreach (string path in Directory.GetFiles(PathManager.Inst.TemplatesPath, "*.ustx"))
            {
                Items.Add(new(path));
            }
            OpenCommand = ReactiveCommand.Create<ProjectTemplateItem>(item => RaiseClose(item.Path));
            DeleteCommand = ReactiveCommand.Create<ProjectTemplateItem>(item =>
            {
                try
                {
                    File.Delete(item.Path);
                    Items.Remove(item);
                }
                catch (Exception exception)
                {
                    ErrorDialogService.Show(new ErrorDialogViewModel(new ErrorMessageNotification(exception)));
                }
            });
            CancelCommand = ReactiveCommand.Create(() => RaiseClose(null));
        }

        public override void RequestBack() => RaiseClose(null);
    }
}
