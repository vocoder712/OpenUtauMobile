using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using OpenUtauMobile.Plugin.Abstractions;
using OpenUtauMobile.Storage;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels
{
    /// <summary>安装表单的类型化输入草稿；不直接写入插件设置。</summary>
    public sealed class PluginFieldViewModel : ReactiveObject, IDisposable
    {
        public PluginFieldDescriptor Descriptor { get; }
        [Reactive] public string Text { get; set; } = "";
        [Reactive] public bool Checked { get; set; }
        [Reactive] public decimal? Number { get; set; }
        [Reactive] public PluginFieldOption? Selected { get; set; }
        [Reactive] public ImmutableArray<string> SelectedIds { get; set; } = [];
        [Reactive] public string Error { get; private set; } = "";
        public ReactiveCommand<Unit, Unit> BrowseCommand { get; }
        private readonly IDisposable subscription;

        public PluginFieldViewModel(PluginFieldDescriptor descriptor, Action changed)
        {
            Descriptor = descriptor;
            switch (descriptor.DefaultValue)
            {
                case PluginValue.Text text: Text = text.Value; break;
                case PluginValue.Path path: Text = path.Value; break;
                case PluginValue.Boolean boolean: Checked = boolean.Value; break;
                case PluginValue.Number number: Number = number.Value; break;
                case PluginValue.Selection selection:
                    Selected = descriptor.Options.IsDefault ? null : descriptor.Options.FirstOrDefault(item => item.Id == selection.Value); break;
                case PluginValue.MultipleSelection selection: SelectedIds = selection.Values.IsDefault ? [] : selection.Values; break;
            }
            BrowseCommand = ReactiveCommand.CreateFromTask(BrowseAsync);
            subscription = this.WhenAnyValue(item => item.Text, item => item.Checked, item => item.Number, item => item.Selected, item => item.SelectedIds)
                .Subscribe(_ => changed());
        }

        private async Task BrowseAsync()
        {
            try
            {
                string path = Descriptor.Kind == PluginFieldKind.Directory
                    ? await FilePicker.PickFolderAsync(Descriptor.Label)
                    : await FilePicker.PickSingleFileAsync(Descriptor.Label,
                        Descriptor.FileExtensions.IsDefault ? [] : [.. Descriptor.FileExtensions.Select(extension => "*" + extension)]);
                if (!string.IsNullOrEmpty(path)) Text = path;
            }
            catch (Exception exception) { Error = exception.Message; }
        }

        /// <summary>提交前校验必填、范围与选项，并保持字段类型。</summary>
        public PluginValue Read()
        {
            Error = "";
            PluginValue value = Descriptor.Kind switch
            {
                PluginFieldKind.Boolean => new PluginValue.Boolean(Checked),
                PluginFieldKind.Number => new PluginValue.Number(Number ?? 0),
                PluginFieldKind.Selection => new PluginValue.Selection(Selected?.Id ?? ""),
                PluginFieldKind.MultipleSelection => new PluginValue.MultipleSelection(SelectedIds),
                PluginFieldKind.File or PluginFieldKind.Directory => new PluginValue.Path(Text, Descriptor.Kind == PluginFieldKind.Directory),
                _ => new PluginValue.Text(Text),
            };
            bool missing = Descriptor.Kind switch
            {
                PluginFieldKind.Boolean => !Checked,
                PluginFieldKind.Number => Number == null,
                PluginFieldKind.Selection => Selected == null,
                PluginFieldKind.MultipleSelection => SelectedIds.IsDefaultOrEmpty,
                _ => string.IsNullOrWhiteSpace(Text),
            };
            if (Descriptor.IsRequired && missing) Error = Helpers.L.S("SingerSetup.RequiredField");
            else if (Number is decimal number && (number < Descriptor.Minimum || number > Descriptor.Maximum))
                Error = Helpers.L.S("SingerSetup.InvalidValue");
            if (!string.IsNullOrEmpty(Error)) throw new SingerInstallException("install.invalid-field", Error, fieldId: Descriptor.Id);
            return value;
        }

        /// <summary>将安装器返回的字段诊断定位到当前输入。</summary>
        internal void SetError(string message) => Error = message;

        public void Dispose() { subscription.Dispose(); BrowseCommand.Dispose(); }
    }
}
