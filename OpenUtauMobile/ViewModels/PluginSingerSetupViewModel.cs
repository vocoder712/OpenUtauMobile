using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Plugin.Abstractions;
using OpenUtauMobile.Services.SingerPlugins;
using OpenUtauMobile.Services.SingerPlugins.Installation;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels
{
    /// <summary>仅承载插件声明式安装步骤，与内置安装流程分离。</summary>
    public sealed class PluginSingerSetupViewModel : NavigateViewModelBase, ISingerInstallationPage, IDisposable
    {
        private readonly SingerInstallController controller;
        private bool initialized;
        private Task? leaveTask;
        private TaskCompletionSource? operationCompletion;
        private readonly IProgress<InstallProgress> progress;
        [Reactive] public bool IsBusy { get; private set; }
        [Reactive] public bool IsClosing { get; private set; }
        /// <summary>安装器未提供有效总量时使用不定进度。</summary>
        [Reactive] public bool IsProgressIndeterminate { get; private set; } = true;
        [Reactive] public double ProgressPercent { get; private set; }
        [Reactive] public bool IsFinished { get; private set; }
        [Reactive] public string Title { get; private set; } = "";
        [Reactive] public string StepText { get; private set; } = "";
        [Reactive] public string Message { get; private set; } = "";
        [Reactive] public string Error { get; private set; } = "";
        [Reactive] public string Summary { get; private set; } = "";
        [Reactive] public bool CanBack { get; private set; }
        [Reactive] public bool CanNext { get; private set; }
        [Reactive] public bool CanPrepare { get; private set; }
        [Reactive] public bool CanInstall { get; private set; }
        [Reactive] public IReadOnlyList<PluginFieldViewModel> Fields { get; private set; } = [];
        [Reactive] public IReadOnlyList<InstallPreviewViewModel> Previews { get; private set; } = [];
        [Reactive] public IReadOnlyList<string> StepTitles { get; private set; } = [];
        [Reactive] public InstallTarget SelectedTarget { get; set; }
        public ImmutableArray<InstallTarget> Targets => controller.Targets;
        public bool HasTargetChoices => Targets.Length > 1;
        public ReactiveCommand<Unit, Unit> NextCommand { get; }
        public ReactiveCommand<Unit, Unit> BackCommand { get; }
        public ReactiveCommand<Unit, Unit> PrepareCommand { get; }
        public ReactiveCommand<Unit, Unit> InstallCommand { get; }
        public ReactiveCommand<Unit, Unit> ExitCommand { get; }
        private readonly IDisposable targetSubscription;

        public PluginSingerSetupViewModel(MainViewModel navigator, RegisteredSingerInstaller installer, string sourcePath) : base(navigator)
        {
            controller = new(installer, sourcePath);
            SelectedTarget = Targets.Single(item => item.Id == controller.DefaultTargetId);
            progress = new Progress<InstallProgress>(item =>
            {
                if (!IsBusy || IsFinished || IsClosing) return;
                Message = !string.IsNullOrWhiteSpace(item.Message) ? item.Message :
                    L.S(item.Phase == InstallPhase.Refreshing ? "Splash.SingerManager" : "SingerSetup.Installing");
                IsProgressIndeterminate = item.Total is not > 0;
                ProgressPercent = item.Total is > 0 ? Math.Clamp(item.Completed * 100d / item.Total.Value, 0, 100) : 0;
            });
            NextCommand = ReactiveCommand.CreateFromTask(() => RunAsync(async () => Apply(await controller.NextAsync(Submission(), progress))),
                this.WhenAnyValue(item => item.CanNext, item => item.IsBusy, item => item.IsClosing, (ready, busy, closing) => ready && !busy && !closing));
            BackCommand = ReactiveCommand.CreateFromTask(() => RunAsync(async () => Apply(await controller.BackAsync())),
                this.WhenAnyValue(item => item.CanBack, item => item.IsBusy, item => item.IsClosing, (ready, busy, closing) => ready && !busy && !closing));
            PrepareCommand = ReactiveCommand.CreateFromTask(() => RunAsync(async () =>
            {
                PreparedInstall plan = await controller.PrepareAsync(Submission(), SelectedTarget.Id, progress);
                Summary = string.Join("\n", new[] { plan.Summary }.Concat(plan.DestinationPaths)
                    .Concat(plan.Diagnostics.IsDefault ? [] : plan.Diagnostics.Select(item => item.Message)));
                CanInstall = true;
            }), this.WhenAnyValue(item => item.CanPrepare, item => item.IsBusy, item => item.IsClosing, (ready, busy, closing) => ready && !busy && !closing));
            InstallCommand = ReactiveCommand.CreateFromTask(() => RunAsync(async () =>
            {
                try
                {
                    InstallResult result = await controller.InstallAsync(controller.Plan!.PlanId, progress);
                    Message = L.S(result.Status == InstallResultStatus.Committed ? "SingerSetup.InstallComplete" : "SingerSetup.PartialFailure");
                    Error = string.Join("\n", result.Diagnostics.IsDefault ? [] : result.Diagnostics.Select(item => item.Message));
                }
                finally
                {
                    IsFinished = true;
                    CanBack = CanNext = CanPrepare = CanInstall = false;
                    ClearContent();
                }
            }), this.WhenAnyValue(item => item.CanInstall, item => item.IsBusy, item => item.IsClosing, (ready, busy, closing) => ready && !busy && !closing));
            ExitCommand = ReactiveCommand.CreateFromTask(LeaveAsync);
            targetSubscription = this.WhenAnyValue(item => item.SelectedTarget).Subscribe(_ => Invalidate());
        }

        public override void OnNavigatedTo()
        {
            if (initialized) return;
            initialized = true;
            _ = RunAsync(async () => Apply(await controller.AnalyzeAsync(progress)));
        }

        private InstallSubmission Submission() => new(controller.View!.CurrentStepId, controller.View.Revision,
            Fields.Where(item => item.Descriptor.IsVisible && item.Descriptor.IsEnabled)
                .ToImmutableDictionary(item => item.Descriptor.Id, item => item.Read(), StringComparer.Ordinal));

        private void Apply(InstallView view)
        {
            ClearContent();
            InstallStep step = view.Steps.Single(item => item.Id == view.CurrentStepId);
            Title = step.Title;
            StepText = string.Format(L.S("SingerSetup.StepFormat"), view.Steps.IndexOf(step) + 1, view.Steps.Length);
            StepTitles = view.Steps.Select(item => item.Title).ToArray();
            Fields = (step.Fields.IsDefault ? ImmutableArray<PluginFieldDescriptor>.Empty : step.Fields)
                .Select(item => new PluginFieldViewModel(item, Invalidate)).ToArray();
            Previews = (step.Content.IsDefault ? ImmutableArray<InstallContent>.Empty : step.Content)
                .Select(item => new InstallPreviewViewModel(item, controller)).ToArray();
            foreach (PluginDiagnostic diagnostic in view.Diagnostics.IsDefault ? [] : view.Diagnostics)
                Fields.FirstOrDefault(item => item.Descriptor.Id == diagnostic.FieldId)?.SetError(diagnostic.Message);
            Error = string.Join("\n", view.Diagnostics.IsDefault ? [] : view.Diagnostics.Select(item => item.Message));
            CanBack = view.CanGoBack;
            CanNext = view.CanGoNext;
            CanPrepare = view.CanPrepare && (view.Diagnostics.IsDefault || view.Diagnostics.All(item => item.Severity != PluginDiagnosticSeverity.Error));
            Invalidate();
        }

        private void Invalidate() { controller.InvalidatePlan(); CanInstall = false; Summary = ""; }

        private async Task RunAsync(Func<Task> action)
        {
            if (IsBusy) return;
            IsBusy = true;
            TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            operationCompletion = completion;
            Error = "";
            try { await action(); }
            catch (OperationCanceledException) { Message = L.S("Common.Cancel"); }
            catch (Exception exception)
            {
                Error = exception.Message;
                if (exception is SingerInstallException failure)
                    Fields.FirstOrDefault(item => item.Descriptor.Id == failure.FieldId)?.SetError(failure.Message);
            }
            finally { IsBusy = false; completion.SetResult(); }
        }

        /// <summary>导航和系统返回共用同一次取消收尾。</summary>
        public Task LeaveAsync() => leaveTask ??= LeaveCoreAsync();
        private async Task LeaveCoreAsync()
        {
            await CloseSessionAsync();
            Navigator.NavigateBack(this);
        }

        /// <summary>主导航移除页面前等待插件操作与临时文件释放。</summary>
        public bool IsSessionClosed { get; private set; }
        public async Task CloseSessionAsync()
        {
            IsClosing = true;
            await controller.DisposeAsync();
            if (operationCompletion != null) await operationCompletion.Task;
            IsSessionClosed = true;
            CanBack = CanNext = CanPrepare = CanInstall = false;
            ClearContent();
        }
        public override void OnBackRequested() { _ = LeaveAsync(); }
        private void ClearContent()
        {
            foreach (PluginFieldViewModel field in Fields) field.Dispose();
            foreach (InstallPreviewViewModel preview in Previews) preview.Dispose();
            Fields = [];
            Previews = [];
        }
        public void Dispose()
        {
            _ = controller.DisposeAsync();
            ClearContent(); targetSubscription.Dispose();
            NextCommand.Dispose(); BackCommand.Dispose(); PrepareCommand.Dispose(); InstallCommand.Dispose(); ExitCommand.Dispose();
        }
    }

    /// <summary>图片在缓存清理前读入内存，不持有文件句柄。</summary>
    public sealed class InstallPreviewViewModel : IDisposable
    {
        public string Title { get; }
        public string Text { get; }
        public Bitmap? Image { get; }
        public bool HasImage => Image != null;
        public bool HasText => !string.IsNullOrEmpty(Text);
        public InstallPreviewViewModel(InstallContent content, SingerInstallController controller)
        {
            Title = content.Title;
            Text = content.Kind == InstallContentKind.Image ? "" : content.Value;
            if (content.Kind != InstallContentKind.Image) return;
            try
            {
                using MemoryStream bytes = new(File.ReadAllBytes(controller.PreviewPath(content.Value)));
                Image = new Bitmap(bytes);
            }
            catch (Exception exception) { Text = exception.Message; }
        }
        public void Dispose() => Image?.Dispose();
    }
}
