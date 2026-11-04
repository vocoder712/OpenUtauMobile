using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core;
using OpenUtauMobile.Plugin.Abstractions;
using Serilog;

namespace OpenUtauMobile.Services.SingerPlugins.Installation
{
    /// <summary>拥有安装会话、版本和临时文件；页面只能驱动串行操作。</summary>
    public sealed class SingerInstallController : IAsyncDisposable
    {
        private readonly SemaphoreSlim gate = new(1, 1);
        private readonly CancellationTokenSource lifetime = new();
        private readonly RegisteredSingerInstaller installer;
        private readonly SingerInstallContext context;
        private readonly InstallFileOperations files;
        private ISingerInstallSession? session;
        private Task? closing;
        private bool finished;
        private bool installStarted;
        private long inputVersion;
        /// <summary>最后一次经过宿主验证的页面快照。</summary>
        public InstallView? View { get; private set; }
        /// <summary>当前唯一可提交计划，输入变化立即清除。</summary>
        public PreparedInstall? Plan { get; private set; }
        /// <summary>本会话可选的配置目标。</summary>
        public ImmutableArray<InstallTarget> Targets => context.InstallTargets;
        /// <summary>遵循宿主当前安装目录设置。</summary>
        public string DefaultTargetId => context.DefaultTargetId;
        /// <summary>预览资源只能从本会话缓存读取。</summary>
        public string PreviewPath(string relativePath) => context.Workspace.GetPath(relativePath);

        public SingerInstallController(RegisteredSingerInstaller installer, string sourcePath)
        {
            this.installer = installer;
            Guid id = Guid.NewGuid();
            ImmutableArray<InstallTarget> targets = [new("configured", Path.GetFullPath(PathManager.Inst.SingersInstallPath),
                PathManager.Inst.SingersInstallPath)];
            string fullSource = Path.GetFullPath(sourcePath);
            ImmutableArray<SingerRoot> roots = SingerPluginRuntime.Roots();
            // 只提供插件目录，配置格式和兼容性由插件负责。
            PluginStorageContext storage = SingerPluginRuntime.Storage(installer.PluginId);
            InstallWorkspace workspace = new(PathManager.Inst.CachePath, id);
            files = new(workspace, targets, id);
            context = new(id, CultureInfo.CurrentUICulture, new InstallSource(fullSource, workspace),
                workspace, roots, targets, targets[0].Id, new InstalledLookup(), files,
                storage, new SingerPluginRuntime.PluginLog(installer.PluginId));
        }

        /// <summary>创建失败与分析失败均等待释放插件句柄，再清理缓存。</summary>
        public Task<InstallView> AnalyzeAsync(IProgress<InstallProgress> progress) => RunAsync(async token =>
        {
            if (session != null || View != null) throw new InvalidOperationException("会话已初始化。");
            try
            {
                session = await installer.Instance.CreateSessionAsync(context, token);
                return Accept(await session.AnalyzeAsync(progress, token));
            }
            catch
            {
                await FinishAsync();
                throw;
            }
        });

        /// <summary>提交当前字段后前进，拒绝过期 Revision。</summary>
        public Task<InstallView> NextAsync(InstallSubmission submission, IProgress<InstallProgress> progress) => RunAsync(async token =>
        {
            RequireSubmission(submission);
            if (!View!.CanGoNext) throw new InvalidOperationException("当前步骤不能前进。");
            Plan = null;
            return Accept(await session!.NextAsync(submission, progress, token));
        });

        /// <summary>回退后旧计划永远失效。</summary>
        public Task<InstallView> BackAsync() => RunAsync(async token =>
        {
            if (View?.CanGoBack != true) throw new InvalidOperationException("当前步骤不能回退。");
            Plan = null;
            return Accept(await session!.BackAsync(View.Revision, token));
        });

        /// <summary>末步输入与目标同时校验，避免摘要遗漏最后一次编辑。</summary>
        public Task<PreparedInstall> PrepareAsync(InstallSubmission submission, string targetId,
            IProgress<InstallProgress> progress) => RunAsync(async token =>
        {
            RequireSubmission(submission);
            if (!View!.CanPrepare || !Targets.Any(item => item.Id == targetId))
                throw new InvalidOperationException("当前状态不能准备安装。");
            Plan = null;
            long version = Interlocked.Read(ref inputVersion);
            PreparedInstall plan = await session!.PrepareAsync(new(targetId), submission, progress, token);
            if (plan.PlanId == Guid.Empty || plan.InstallerId != installer.Descriptor.Id || plan.TargetId != targetId ||
                plan.Revision != View.Revision || plan.DestinationPaths.IsDefaultOrEmpty ||
                (!plan.Diagnostics.IsDefault && plan.Diagnostics.Any(item => item.Severity == PluginDiagnosticSeverity.Error)))
                throw new SingerInstallException("install.invalid-plan", "安装计划无效。");
            InstallTarget target = Targets.Single(item => item.Id == targetId);
            foreach (string path in plan.DestinationPaths)
            {
                InstallPaths.RequireInside(target.Path, path);
                InstallFileOperations.RequireAbsent(path);
            }
            if (version != Interlocked.Read(ref inputVersion))
                throw new SingerInstallException("install.stale-plan", "输入已变化，请重新检查安装。");
            Plan = plan;
            return plan;
        });

        /// <summary>字段或目标编辑后不允许提交旧摘要。</summary>
        public void InvalidatePlan()
        {
            Interlocked.Increment(ref inputVersion);
            Plan = null;
        }

        /// <summary>计划只消费一次；发布后的异常返回真实部分成功结果。</summary>
        public Task<InstallResult> InstallAsync(Guid planId, IProgress<InstallProgress> progress) => RunAsync(async token =>
        {
            if (Plan?.PlanId != planId || installStarted) throw new InvalidOperationException("安装计划已失效。");
            PreparedInstall plan = Plan;
            Plan = null;
            installStarted = true;
            InstallResult result = await SingerDiscoveryService.Instance.WithCatalogLockAsync(async () =>
            {
                InstallResult committed;
                try
                {
                    token.ThrowIfCancellationRequested();
                    files.PlannedPaths = plan.DestinationPaths;
                    files.CanPublish = true;
                    committed = await session!.InstallAsync(planId, progress, token);
                    if (files.Published.IsDefaultOrEmpty) throw new IOException("安装器未返回正式发布回执。");
                    committed = committed with { Locations = files.Published };
                    if (plan.DestinationPaths.Any(path => files.Published.All(item =>
                        !string.Equals(item.Path, Path.GetFullPath(path), InstallPaths.Comparison))))
                        committed = AddFailure(committed, "install.incomplete", "安装器未提交计划中的全部内容。");
                }
                catch (Exception exception) when (!files.Published.IsDefaultOrEmpty)
                {
                    committed = new(InstallResultStatus.PartialFailure, files.Published, plan.Singers,
                        [new("install.partial", exception.Message)]);
                }
                finally
                {
                    files.CanPublish = false;
                    await FinishAsync();
                }
                return committed;
            }, token);
            try
            {
                await ServiceHub.FlushFileSystemAsync();
                progress.Report(new(InstallPhase.Refreshing, ""));
                await SingerDiscoveryService.Instance.RefreshAsync();
                foreach (SingerReference singer in result.Singers.IsDefault ? [] : result.Singers)
                {
                    ImmutableArray<InstalledSingerLocation> matches = await context.InstalledSingers.FindAsync(singer, CancellationToken.None);
                    if (matches.All(item => item.Availability != SingerAvailability.Ready))
                        result = AddFailure(result, "install.not-discovered", $"已安装，但读取失败：{singer.SingerId}");
                }
            }
            catch (Exception exception) { result = AddFailure(result, "install.refresh-failed", exception.Message); }
            return result;
        });

        private static InstallResult AddFailure(InstallResult result, string code, string message) => result with
        {
            Status = InstallResultStatus.PartialFailure,
            Diagnostics = (result.Diagnostics.IsDefault ? ImmutableArray<PluginDiagnostic>.Empty : result.Diagnostics).Add(new(code, message)),
        };

        private void RequireSubmission(InstallSubmission submission)
        {
            if (View == null || session == null || submission.ExpectedRevision != View.Revision || submission.StepId != View.CurrentStepId)
                throw new SingerInstallException("install.stale-view", "安装步骤已变化，请重新检查输入。");
        }

        private InstallView Accept(InstallView view)
        {
            if (view.Steps.IsDefaultOrEmpty || view.Steps.Select(item => item.Id).Distinct().Count() != view.Steps.Length ||
                !view.Steps.Any(item => item.Id == view.CurrentStepId) || (View != null && view.Revision <= View.Revision))
                throw new SingerInstallException("install.invalid-view", "安装器返回了无效步骤。");
            foreach (InstallStep step in view.Steps)
            {
                if (string.IsNullOrWhiteSpace(step.Id) || string.IsNullOrWhiteSpace(step.Title))
                    throw new SingerInstallException("install.invalid-step", "安装步骤缺少标识或名称。");
                SingerPluginRegistration.ValidateFields(step.Fields.IsDefault ? [] : step.Fields);
            }
            View = view;
            return view;
        }

        private async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation)
        {
            await gate.WaitAsync(lifetime.Token);
            try
            {
                ObjectDisposedException.ThrowIf(finished, this);
                return await Task.Run(() => operation(lifetime.Token));
            }
            finally { gate.Release(); }
        }

        private async Task FinishAsync()
        {
            if (finished) return;
            finished = true;
            try { if (session != null) await session.DisposeAsync(); }
            catch (Exception exception) { Log.Warning(exception, "Failed to dispose singer installation session"); }
            finally { files.Cleanup(); }
        }

        /// <summary>发出取消后等待正在执行的插件操作返回，不并发删除缓存。</summary>
        public ValueTask DisposeAsync()
        {
            lock (gate) return new(closing ??= CloseAsync());
        }

        private async Task CloseAsync()
        {
            lifetime.Cancel();
            await gate.WaitAsync();
            try { await FinishAsync(); }
            finally { gate.Release(); }
        }

        private sealed class InstalledLookup : IInstalledSingerLookup
        {
            public ValueTask<ImmutableArray<InstalledSingerLocation>> FindAsync(SingerReference singer, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(SingerDiscoveryService.Instance.Candidates
                    .Where(item => item.ProviderId == singer.ProviderId && item.Entry.Singer?.Id == singer.SingerId)
                    .Select(item => new InstalledSingerLocation(singer, item.Entry.Location, item.Entry.Availability)).ToImmutableArray());
            }
        }
    }
}
