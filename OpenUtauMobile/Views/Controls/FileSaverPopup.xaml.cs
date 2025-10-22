using CommunityToolkit.Maui.Views;
using OpenUtauMobile.ViewModels.Controls;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;

namespace OpenUtauMobile.Views.Controls
{
    public partial class FileSaverPopup : Popup
    {
        private readonly FileSaverPopupViewModel _viewModel;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="types">限制文件类型，例如[*.ustx, *.wav]</param>
        /// <param name="initialDirectory">初始加载目录</param>
        /// <param name="initialFileName">默认文件名</param>
        public FileSaverPopup(string[] types, string initialDirectory = "", string initialFileName = "")
        {
            InitializeComponent();
            _viewModel = (FileSaverPopupViewModel)BindingContext;
            _viewModel.Types = types;
            _viewModel.CurrentDirectory = initialDirectory;
            _viewModel.FileName = initialFileName;
            _viewModel.Initialize();
        }

        /// <summary>
        /// UI点击项目事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnItemSelected(object sender, SelectionChangedEventArgs e)
        {
            if (_viewModel.SelectedItem is DirectoryInfo directory) // 选中的是目录
            {
                _viewModel.CurrentDirectory = directory.FullName;
            }
            else if (_viewModel.SelectedItem is FileInfo file) // 选中的是文件
            {
                _viewModel.FileName = file.Name;
            }
        }

        /// <summary>
        /// 返回上一级目录
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnGoUpClicked(object sender, EventArgs e)
        {
            DirectoryInfo? parentDirectory = Directory.GetParent(_viewModel.CurrentDirectory);
            if (parentDirectory != null)
            {
                _viewModel.CurrentDirectory = parentDirectory.FullName;
            }
        }

        /// <summary>
        /// 取消按钮点击事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnCancelClicked(object sender, EventArgs e)
        {
            CloseAsync("");
        }

        /// <summary>
        /// 刷新当前目录
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ButtonRefresh_Clicked(object sender, EventArgs e)
        {
            _viewModel.LoadDirectory(_viewModel.CurrentDirectory);
        }

        /// <summary>
        /// 保存
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ButtonSave_Clicked(object sender, EventArgs e)
        {
            // 先判断文件名是否符合types
            if (_viewModel.Types.Length > 0 && _viewModel.Types[0] != "*")
            {
                bool isValid = false;
                foreach (string type in _viewModel.Types)
                {
                    if (_viewModel.FileName.EndsWith(type.TrimStart('*'), StringComparison.OrdinalIgnoreCase)) // 忽略大小写比较
                    {
                        isValid = true;
                        break;
                    }
                }
                // 如果不符合类型要求，自动添加第一个类型后缀
                if (!isValid)
                {
                    _viewModel.FileName = _viewModel.FileName + (_viewModel.Types[0].StartsWith('*') ? _viewModel.Types[0].TrimStart('*') : _viewModel.Types[0]);
                }
            }
            CloseAsync(Path.Combine(_viewModel.CurrentDirectory, _viewModel.FileName));
        }
    }
}