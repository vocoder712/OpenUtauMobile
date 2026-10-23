using Avalonia.Controls;
using Avalonia.Input;
using System;
using OpenUtauMobile.Controls;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Views
{
    /// <summary>沙箱管理导航页，复用应用主题的列表、按钮和弹窗角色。</summary>
    public partial class SandboxFileManagerView : UserControl
    {
        /// <summary>加载管理页面及其局部样式。</summary>
        public SandboxFileManagerView()
        {
            InitializeComponent();
            CreateMenu.ActionInvoked += CreateAction;
            KeyDown += (_, args) =>
            {
                if (args.Key == Key.Escape && DataContext is SandboxFileManagerViewModel viewModel)
                {
                    viewModel.OnBackRequested();
                    args.Handled = true;
                }
            };
        }

        /// <summary>FAB 动作采用表情图库相同的命令分发，菜单先关闭再打开选择器。</summary>
        private void CreateAction(FabMenuAction action)
        {
            if (DataContext is SandboxFileManagerViewModel { CanModify: true } viewModel)
                viewModel.CreationCommand.Execute(action.Id).Subscribe();
        }
    }
}