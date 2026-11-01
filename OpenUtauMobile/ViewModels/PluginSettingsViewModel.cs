using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Plugin.Abstractions;
using OpenUtauMobile.Services.SingerPlugins;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace OpenUtauMobile.ViewModels
{
    /// <summary>设置页内的插件字段，变更后串行校验保存，不提供独立保存或取消流程。</summary>
    public sealed class PluginSettingsViewModel : ViewModelBase, IDisposable
    {
        private readonly RegisteredPluginSettings registration;
        private PluginSettingsSession? session;
        private CancellationTokenSource? operation;
        private bool disposed;
        private long editRevision;
        private long pendingRevision;
        private ImmutableDictionary<string, PluginValue>? pendingValues;
        private Task saveTask = Task.CompletedTask;
        [Reactive] public ImmutableArray<PluginFieldViewModel> Fields { get; private set; } = [];
        [Reactive] public bool IsBusy { get; private set; }
        [Reactive] public bool IsSaving { get; private set; }
        [Reactive] public bool IsLoaded { get; private set; }
        [Reactive] public string Error { get; private set; } = "";
        [Reactive] public string Message { get; private set; } = "";

        /// <summary>绑定一个插件的设置，初始化时异步读取文件。</summary>
        public PluginSettingsViewModel(RegisteredPluginSettings registration)
        {
            this.registration = registration;
        }

        /// <summary>由设置页选中插件后调用，初始化不触发保存。</summary>
        public Task InitializeAsync() => IsLoaded ? Task.CompletedTask : RunAsync(async token =>
        {
            session = await Task.Run(() => PluginSettingsService.Open(registration), token);
            token.ThrowIfCancellationRequested();
            if (disposed) return;
            Fields = [.. registration.Descriptor.Fields.Select(item => new PluginFieldViewModel(
                session.Values.TryGetValue(item.Field.Id, out PluginValue? value) ? item.Field with { DefaultValue = value } : item.Field,
                OnFieldChanged))];
            IsLoaded = true;
        });

        private ImmutableDictionary<string, PluginValue> ReadValues() => session!.Values.SetItems(
            Fields.Select(field => KeyValuePair.Create(field.Descriptor.Id, field.Read())));

        /// <summary>在界面线程截取不可变快照；快速连续修改只保留尚未提交的最新一份。</summary>
        private void OnFieldChanged()
        {
            if (!IsLoaded || disposed) return;
            editRevision++;
            Error = "";
            Message = "";
            try { pendingValues = ReadValues(); pendingRevision = editRevision; }
            catch (Exception exception) { pendingValues = null; ShowError(exception); return; }
            if (!IsSaving) saveTask = SavePendingAsync();
        }

        /// <summary>串行持久化，不重建字段或禁用输入，防止旧值覆盖新值及焦点跳动。</summary>
        private async Task SavePendingAsync()
        {
            IsSaving = true;
            try
            {
                while (pendingValues is { } values)
                {
                    long revision = pendingRevision;
                    pendingValues = null;
                    try
                    {
                        await session!.SaveAsync(values);
                        await NotifyCommittedAsync();
                        if (!disposed && revision == editRevision)
                        {
                            Error = "";
                            Message = registration.Descriptor.Fields.Any(field => field.Application == PluginSettingApplication.Restart)
                                ? L.S("PluginSettings.SavedRestart") : "";
                        }
                    }
                    catch (Exception exception)
                    {
                        if (disposed) Log.Warning(exception, "Failed to persist plugin settings {Plugin}", registration.PluginId);
                        else if (revision == editRevision) ShowError(exception);
                    }
                }
            }
            finally { IsSaving = false; }
        }

        /// <summary>切换插件或离开设置页时等待已经排队的变更，不出现草稿确认。</summary>
        public Task FlushAsync() => saveTask;

        private async Task NotifyCommittedAsync()
        {
            await Services.ServiceHub.FlushFileSystemAsync();
            if (registration.Descriptor.Fields.Any(field => field.Application == PluginSettingApplication.ReloadResources))
                await SingerDiscoveryService.Instance.RefreshAsync();
        }

        private async Task RunAsync(Func<CancellationToken, Task> action)
        {
            if (IsBusy || disposed) return;
            using CancellationTokenSource cancellation = new();
            operation = cancellation;
            IsBusy = true;
            Error = "";
            Message = "";
            try { await action(cancellation.Token); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception exception) { if (!disposed) ShowError(exception); }
            finally { operation = null; IsBusy = false; }
        }

        private void ShowError(Exception exception)
        {
            Error = exception.Message;
            if (exception is PluginSettingsValidationException validation)
                foreach (PluginDiagnostic diagnostic in validation.Diagnostics)
                    Fields.FirstOrDefault(field => field.Descriptor.Id == diagnostic.FieldId)?.SetError(
                        diagnostic.Code == "settings.invalid-field" ? L.S("SingerSetup.InvalidValue") : diagnostic.Message);
        }

        public void Dispose()
        {
            disposed = true;
            operation?.Cancel();
            // 已捕获的保存快照继续写完，离开页面不会撤销已输入的有效设置。
            foreach (PluginFieldViewModel field in Fields) field.Dispose();
        }
    }
}
