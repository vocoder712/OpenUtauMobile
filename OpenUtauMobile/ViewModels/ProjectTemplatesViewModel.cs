using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reactive;
using System.Threading.Tasks;
using OpenUtau.Core;
using OpenUtauMobile.Services.Dialogs;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels
{
    public sealed record ProjectTemplateItem(string Path)
    {
        public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
    }

    /// <summary>异步读取模板列表，关闭后不发布迟到的目录结果。</summary>
    public sealed class ProjectTemplatesViewModel : PopupViewModelBase
    {
        private bool closed;
        /// <summary>模板目录正在后台读取。</summary>
        [Reactive] public bool IsLoading { get; private set; } = true;
        /// <summary>加载结束后才显示空列表提示。</summary>
        [Reactive] public bool ShowEmpty { get; private set; }
        /// <summary>目录读取失败时显示错误，空列表仍复用动态本地化资源。</summary>
        [Reactive] public bool HasLoadError { get; private set; }
        /// <summary>目录读取失败的具体说明。</summary>
        [Reactive] public string LoadError { get; private set; } = string.Empty;
        public ObservableCollection<ProjectTemplateItem> Items { get; } = [];
        public ReactiveCommand<ProjectTemplateItem, Unit> OpenCommand { get; }
        public ReactiveCommand<ProjectTemplateItem, Unit> DeleteCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }

        public ProjectTemplatesViewModel()
        {
            OpenCommand = ReactiveCommand.Create<ProjectTemplateItem>(item => Close(item.Path));
            DeleteCommand = ReactiveCommand.Create<ProjectTemplateItem>(item =>
            {
                try
                {
                    File.Delete(item.Path);
                    Items.Remove(item);
                    ShowEmpty = Items.Count == 0;
                }
                catch (Exception exception)
                {
                    ErrorDialogService.Show(new ErrorDialogViewModel(new ErrorMessageNotification(exception)));
                }
            });
            CancelCommand = ReactiveCommand.Create(RequestBack);
            _ = LoadAsync();
        }

        /// <summary>浏览模板只读目录，不因空目录创建产生额外磁盘写入。</summary>
        private async Task LoadAsync()
        {
            string directory = PathManager.Inst.TemplatesPath;
            try
            {
                string[] paths = await Task.Run(() => Directory.Exists(directory)
                    ? Directory.GetFiles(directory, "*.ustx") : []);
                if (closed) return;
                foreach (string path in paths) Items.Add(new(path));
            }
            catch (Exception exception)
            {
                if (closed) return;
                LoadError = exception.Message;
                HasLoadError = true;
            }
            finally
            {
                if (!closed)
                {
                    IsLoading = false;
                    ShowEmpty = Items.Count == 0 && !HasLoadError;
                }
            }
        }

        /// <summary>用户关闭弹窗后，读取可以结束但不再更新弹窗。</summary>
        private void Close(string? path)
        {
            closed = true;
            RaiseClose(path);
        }

        public override void RequestBack() => Close(null);
    }
}
