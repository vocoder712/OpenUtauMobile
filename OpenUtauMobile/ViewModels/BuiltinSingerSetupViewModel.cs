using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.Services.SingerInstallation;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace OpenUtauMobile.ViewModels
{
    /// <summary>内置编码预览、可取消安装及结果页；类型始终由入口确定。</summary>
    public abstract class BuiltinSingerSetupViewModel : NavigateViewModelBase, ISingerInstallationPage, IDisposable
    {
        private const int previewLimit = 50;
        private readonly BuiltinSingerInstaller installer;
        private readonly CompositeDisposable subscriptions = new();
        private readonly CancellationTokenSource lifetime = new();
        private readonly SemaphoreSlim previewGate = new(1, 1);
        private readonly List<Task> previewOperations = [];
        private CancellationTokenSource? previewCancellation;
        private CancellationTokenSource? installCancellation;
        private Task installation = Task.CompletedTask;
        private Task? closing;
        private bool initialized;
        private bool textEncodingEdited;
        private bool applyingEncoding;
        private bool configuredEncodingLoaded;
        private bool disposed;
        private long previewVersion;
        private static readonly string[] viewProperties =
        [
            nameof(CanInstallAtStep), nameof(CanNext), nameof(CanBack), nameof(ShowArchiveEncoding),
            nameof(ShowTextEncoding), nameof(ShowPreview), nameof(ShowPackageMetadata), nameof(ShowPackageMetadataError), nameof(ShowForm), nameof(IsSuccessful), nameof(ResultTitle), nameof(StepText),
        ];

        private sealed record PreviewResult(IReadOnlyList<string> Items, string? TextEncoding,
            (string Name, string Version)? Package = null);
        public string ArchiveFilePath { get; }
        public string FileName => Path.GetFileName(ArchiveFilePath);
        public string InstallerName => installer.DisplayName;
        public bool IsArchive => installer.IsArchive;
        public Encoding[] Encodings { get; } =
        [
            Encoding.GetEncoding("shift_jis"), Encoding.UTF8, Encoding.GetEncoding("gb2312"),
            Encoding.GetEncoding("big5"), Encoding.GetEncoding("ks_c_5601-1987"),
            Encoding.GetEncoding("Windows-1252"), Encoding.GetEncoding("macintosh"),
        ];
        [Reactive] public Encoding ArchiveEncoding { get; set; }
        [Reactive] public Encoding TextEncoding { get; set; }
        /// <summary>预选类型不接受配置文件触发的切换。</summary>
        public string SingerType => installer.SingerType ?? "";
        [Reactive] public int Step { get; private set; }
        [Reactive] public IReadOnlyList<string> TextItems { get; private set; } = [];
        /// <summary>单文件安装确认中的歌手名称，不与编码样例混用。</summary>
        [Reactive] public string PackageName { get; private set; } = "";
        /// <summary>安装包声明的声库版本，缺失时不显示对应信息行。</summary>
        [Reactive] public string PackageVersion { get; private set; } = "";
        [Reactive] public bool IsPreviewLoading { get; private set; }
        [Reactive] public bool HasPreviewError { get; private set; }
        [Reactive] public bool IsBusy { get; private set; }
        [Reactive] public bool IsCancelling { get; private set; }
        [Reactive] public bool IsClosing { get; private set; }
        [Reactive] public bool IsFinished { get; private set; }
        [Reactive] public bool IsCancelled { get; private set; }
        [Reactive] public bool HasFailed { get; private set; }
        [Reactive] public bool IsProgressIndeterminate { get; private set; }
        [Reactive] public double ProgressPercent { get; private set; }
        [Reactive] public string Message { get; private set; } = "";
        [Reactive] public string Error { get; private set; } = "";
        public bool ShowForm => !IsFinished && !IsBusy;
        public bool CanInstallAtStep => ShowForm && (!IsArchive || Step == 1);
        public bool CanNext => ShowForm && IsArchive && Step == 0;
        public bool CanBack => !IsFinished && IsArchive && Step > 0;
        public bool ShowArchiveEncoding => ShowForm && IsArchive && Step == 0;
        public bool ShowTextEncoding => ShowForm && IsArchive && Step == 1;
        public bool ShowPreview => ShowForm && IsArchive;
        /// <summary>单文件包读取成功后展示带标签的元数据确认区域。</summary>
        public bool ShowPackageMetadata => ShowForm && !IsArchive && !IsPreviewLoading && !HasPreviewError;
        /// <summary>元数据读取失败仅显示简短提示，不作为单文件安装的前置条件。</summary>
        public bool ShowPackageMetadataError => ShowForm && !IsArchive && !IsPreviewLoading && HasPreviewError;
        /// <summary>结果页成功状态，与取消、失败状态互斥。</summary>
        public bool IsSuccessful => IsFinished && !IsCancelled && !HasFailed;
        public string StepText => IsFinished ? "" : IsArchive ? string.Format(L.S("SingerSetup.StepFormat"), Step + 1, 2) : "";
        public string ResultTitle => L.S(IsCancelled ? "SingerSetup.Cancelled" : HasFailed ? "SingerSetup.Failed" : "SingerSetup.Success");
        public ReactiveCommand<Unit, Unit> NextCommand { get; }
        public ReactiveCommand<Unit, Unit> BackCommand { get; }
        public ReactiveCommand<Unit, Unit> InstallCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }
        public ReactiveCommand<Unit, Unit> ExitCommand { get; }

        protected BuiltinSingerSetupViewModel(MainViewModel navigator, BuiltinSingerInstaller installer, string sourcePath)
            : base(navigator)
        {
            this.installer = installer;
            ArchiveFilePath = sourcePath;
            ArchiveEncoding = Encodings[0];
            TextEncoding = Encodings[0];
            IObservable<bool> editable = this.WhenAnyValue(item => item.IsBusy, item => item.IsClosing,
                item => item.IsFinished, item => item.IsPreviewLoading, item => item.HasPreviewError,
                (busy, leaving, finished, loading, failed) => !busy && !leaving && !finished && !loading && (!IsArchive || !failed));
            NextCommand = ReactiveCommand.Create(() => { Step++; }, editable.CombineLatest(
                this.WhenAnyValue(item => item.Step).Select(_ => Step == 0 && IsArchive), (ready, next) => ready && next));
            BackCommand = ReactiveCommand.Create(() => { if (TryLeave() && Step > 0) Step--; },
                this.WhenAnyValue(item => item.Step, item => item.IsClosing, item => item.IsFinished,
                    (step, leaving, finished) => step > 0 && !leaving && !finished));
            InstallCommand = ReactiveCommand.CreateFromTask(() => installation = InstallCoreAsync(), editable.CombineLatest(
                this.WhenAnyValue(item => item.Step).Select(_ => !IsArchive || Step == 1), (ready, install) => ready && install));
            CancelCommand = ReactiveCommand.Create(() =>
            {
                IsCancelling = true;
                IsProgressIndeterminate = true;
                Message = L.S("SingerSetup.RollingBack");
                installCancellation?.Cancel();
            }, this.WhenAnyValue(item => item.IsBusy, item => item.IsCancelling, (busy, canceling) => busy && !canceling));
            ExitCommand = ReactiveCommand.CreateFromTask(LeaveAsync);
            this.WhenAnyValue(item => item.Step, item => item.IsFinished, item => item.IsBusy,
                item => item.IsPreviewLoading, item => item.HasPreviewError).Subscribe(_ =>
            {
                foreach (string name in viewProperties) this.RaisePropertyChanged(name);
            }).DisposeWith(subscriptions);
            this.WhenAnyValue(item => item.Step, item => item.ArchiveEncoding, item => item.TextEncoding)
                .Skip(1).Subscribe(_ =>
                {
                    if (initialized && IsArchive && !applyingEncoding && !IsBusy && !IsClosing && !IsFinished) StartPreview();
                }).DisposeWith(subscriptions);
            this.WhenAnyValue(item => item.TextEncoding).Skip(1).Subscribe(_ =>
            {
                if (!applyingEncoding) textEncodingEdited = true;
            }).DisposeWith(subscriptions);
        }

        /// <summary>直接进入页面，仅异步生成有限预览，不先校验声库类型或遍历所有内容。</summary>
        public override void OnNavigatedTo()
        {
            if (initialized) return;
            initialized = true;
            StartPreview();
        }

        private void StartPreview()
        {
            previewOperations.RemoveAll(task => task.IsCompleted);
            previewOperations.Add(RefreshPreviewAsync());
        }

        /// <summary>归档打开和文本读取均在后台串行运行；旧任务不覆盖较新的编码选择。</summary>
        private async Task RefreshPreviewAsync()
        {
            previewCancellation?.Cancel();
            CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            previewCancellation = cancellation;
            CancellationToken token = cancellation.Token;
            long version = ++previewVersion;
            int step = Step;
            Encoding archiveEncoding = ArchiveEncoding;
            Encoding textEncoding = TextEncoding;
            bool readEncoding = !textEncodingEdited && !configuredEncodingLoaded;
            IsPreviewLoading = true;
            HasPreviewError = false;
            Error = "";
            Message = L.S(!IsArchive ? "SingerSetup.ReadingMetadata" : step == 0 ? "SingerSetup.Step1.Preview" : "SingerSetup.Step2.Preview");
            try
            {
                await previewGate.WaitAsync(token);
                PreviewResult result;
                try { result = await Task.Run(() => ReadPreview(step, archiveEncoding, textEncoding, readEncoding, token), token); }
                finally { previewGate.Release(); }
                if (version != previewVersion || IsClosing) return;
                TextItems = result.Items;
                PackageName = result.Package?.Name ?? "";
                PackageVersion = result.Package?.Version ?? "";
                HasPreviewError = !IsArchive && result.Package == null;
                if (!textEncodingEdited && result.TextEncoding is { Length: > 0 } encoding)
                {
                    try
                    {
                        applyingEncoding = true;
                        TextEncoding = Encoding.GetEncoding(encoding);
                        configuredEncodingLoaded = true;
                    }
                    catch (ArgumentException) { }
                    finally { applyingEncoding = false; }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception exception)
            {
                if (version != previewVersion || IsClosing) return;
                Error = IsArchive ? exception.Message : "";
                PackageName = "";
                PackageVersion = "";
                HasPreviewError = true;
            }
            finally
            {
                if (version == previewVersion)
                {
                    IsPreviewLoading = false;
                    Message = "";
                }
                if (ReferenceEquals(previewCancellation, cancellation)) previewCancellation = null;
                cancellation.Dispose();
            }
        }

        /// <summary>文件名和文本样例分别最多产生 50 项，达到上限就停止读取后续内容。</summary>
        private PreviewResult ReadPreview(int step, Encoding archiveEncoding, Encoding textEncoding, bool readEncoding, CancellationToken token)
        {
            if (!IsArchive)
            {
                return new([], null, VogenPackage.ReadMetadata(ArchiveFilePath, token));
            }
            using FileStream input = File.OpenRead(ArchiveFilePath);
            using IArchive archive = ArchiveFactory.OpenArchive(input, new ReaderOptions
            {
                LeaveStreamOpen = false,
                ArchiveEncoding = new ArchiveEncoding { Forced = archiveEncoding },
            });
            List<string> items = [];
            string? suggestedEncoding = null;
            int textSamples = 0;
            foreach (IArchiveEntry entry in archive.Entries)
            {
                token.ThrowIfCancellationRequested();
                if (items.Count >= previewLimit || textSamples >= previewLimit) break;
                string key = entry.Key?.Replace('\\', '/') ?? "";
                if (step == 0)
                {
                    items.Add(key);
                    if (readEncoding && suggestedEncoding == null && Path.GetFileName(key) == "character.yaml")
                    {
                        // 编码提示仅来自当前样例中的可读配置，不把解析失败变成安装前校验。
                        try
                        {
                            using Stream config = entry.OpenEntryStream();
                            suggestedEncoding = VoicebankConfig.Load(config)?.TextFileEncoding;
                        }
                        catch (Exception exception) when (exception is not OperationCanceledException) { }
                    }
                }
                else if (!entry.IsDirectory && (key.EndsWith("character.txt", StringComparison.OrdinalIgnoreCase) || key.EndsWith("oto.ini", StringComparison.OrdinalIgnoreCase)))
                {
                    using Stream stream = entry.OpenEntryStream();
                    using StreamReader reader = new(stream, textEncoding);
                    items.Add($"------ {key} ------");
                    textSamples++;
                    while (textSamples < previewLimit && !reader.EndOfStream)
                    {
                        token.ThrowIfCancellationRequested();
                        string? line = reader.ReadLine();
                        textSamples++;
                        if (!string.IsNullOrWhiteSpace(line)) items.Add(line);
                    }
                }
            }
            return new(items, suggestedEncoding);
        }

        /// <summary>安装取消后等待回滚完成，任何失败均进入结果页；取消失败同时弹窗。</summary>
        private async Task InstallCoreAsync()
        {
            IsBusy = true;
            Error = "";
            Message = L.S("SingerSetup.Installing");
            installCancellation = new();
            IProgress<(double Percent, string Message)> progress = new Progress<(double Percent, string Message)>(item =>
            {
                if (!IsBusy || IsSessionClosed) return;
                IsProgressIndeterminate = item.Percent < 0;
                ProgressPercent = Math.Clamp(item.Percent, 0, 100);
                Message = item.Message;
            });
            try
            {
                await BuiltinSingerInstallers.InstallAsync(installer, ArchiveFilePath, SingerType, ArchiveEncoding, TextEncoding, progress, installCancellation.Token);
                Message = L.S("SingerSetup.InstallComplete");
            }
            catch (OperationCanceledException) when (installCancellation.IsCancellationRequested)
            {
                IsCancelled = true;
                Message = L.S("SingerSetup.Cancelled");
            }
            catch (Exception exception)
            {
                HasFailed = true;
                Error = exception.Message;
                Message = L.S("SingerSetup.Failed");
                ErrorDialogService.Show(new ErrorDialogViewModel(new ErrorMessageNotification(exception)));
            }
            finally
            {
                IsFinished = true;
                IsBusy = false;
                installCancellation.Dispose();
                installCancellation = null;
                TextItems = [];
            }
        }

        /// <summary>所有返回入口在安装和回滚阶段均提示用户先取消，不自动离开。</summary>
        public bool TryLeave()
        {
            if (!IsBusy) return true;
            ToastService.Enqueue(L.S("SingerSetup.InstallInProgress"));
            return false;
        }

        public bool IsSessionClosed { get; private set; }
        public Task CloseSessionAsync() => closing ??= CloseCoreAsync();
        private async Task CloseCoreAsync()
        {
            IsClosing = true;
            lifetime.Cancel();
            await Task.WhenAll(previewOperations);
            await installation;
            IsSessionClosed = true;
        }

        private async Task LeaveAsync()
        {
            if (!TryLeave()) return;
            await CloseSessionAsync();
            Navigator.NavigateBack(this);
        }

        public override void OnBackRequested() => _ = LeaveAsync();

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel();
            subscriptions.Dispose();
            NextCommand.Dispose();
            BackCommand.Dispose();
            InstallCommand.Dispose();
            CancelCommand.Dispose();
            ExitCommand.Dispose();
        }
    }
}
