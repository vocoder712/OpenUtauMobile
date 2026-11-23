using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtauMobile.Services.Projects;

/// <summary>保存阶段由功能调用方本地化，后台不读取 UI 资源。</summary>
internal enum ProjectSaveStage { Capturing, Waiting, Preparing, Serializing, Writing, Replacing, Synchronizing, Confirming }

/// <summary>手动、模板及自动保存共用的单写入队列；活动工程只在 UI 捕获和确认阶段访问。</summary>
internal sealed class ProjectSaveCoordinator
{
    // 保存独占实例在后台首次创建，避免默认序列化器的锁阻塞 UI 上的表达式图指纹计算。
    private static readonly Lazy<Yaml> SaveSerializer = new(() => new Yaml());
    /// <summary>所有保存入口共用进程内唯一的工程写入队列。</summary>
    public static ProjectSaveCoordinator Inst { get; } = new();
    private readonly SemaphoreSlim _writer = new(1, 1);
    private bool _autoSaveRunning;
    private int _manualRequests;
    private string _backupPath = string.Empty;
    private long _recoveryGeneration;

    /// <summary>用户明确丢弃恢复时使在途自动任务失去发布恢复提示的资格。</summary>
    public void DismissRecovery()
    {
        Dispatcher.UIThread.VerifyAccess();
        _recoveryGeneration++;
    }

    /// <summary>调用方在模态窗口内等待整个事务；模板保存不会更改活动工程保存点。</summary>
    public async Task<bool> SaveManualAsync(DocManager.SaveCheckpoint owner, string path, bool template,
        IProgress<ProjectSaveStage>? progress = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        _manualRequests++;
        try { return await SaveAsync(owner, path, template, autoSave: false, progress); }
        finally { _manualRequests--; }
    }

    /// <summary>无编辑、开放撤销组、有手动请求或已有自动任务时跳过，不积压快照。</summary>
    public async Task<bool> AutoSaveAsync(DocManager.SaveCheckpoint owner)
    {
        Dispatcher.UIThread.VerifyAccess();
        DocManager doc = DocManager.Inst;
        if (_autoSaveRunning || _manualRequests != 0 || !doc.IsSaveCheckpointCurrent(owner) || doc.HasOpenUndoGroup)
            return false;
        DocManager.SaveCheckpoint point = doc.CaptureSaveCheckpoint();
        if (!doc.NeedsAutoSave(point)) return false;
        if (string.IsNullOrEmpty(_backupPath))
        {
            // DocManager 只拥有一个活动工程，队列统一替换同一个恢复文件，避免重开工程积累备份。
            _backupPath = Path.Combine(PathManager.Inst.BackupsPath, "Mobile-autosave.ustx");
        }
        _autoSaveRunning = true;
        try { return await SaveAsync(owner, _backupPath, template: false, autoSave: true, progress: null); }
        catch (Exception error)
        {
            // 不推进检查点，下一次定时请求仍能重试；不弹模态打断编辑。
            Log.Error(error, "自动保存失败");
            return false;
        }
        finally { _autoSaveRunning = false; }
    }

    /// <summary>捕获开始时版本，后台准备及写入，最后按文档会话确认结果。</summary>
    private async Task<bool> SaveAsync(DocManager.SaveCheckpoint owner, string path, bool template, bool autoSave,
        IProgress<ProjectSaveStage>? progress)
    {
        DocManager doc = DocManager.Inst;
        if (!doc.IsSaveCheckpointCurrent(owner) || doc.HasOpenUndoGroup || string.IsNullOrWhiteSpace(path)) return false;
        progress?.Report(ProjectSaveStage.Capturing);
        string previousRecovery = Preferences.Default.RecoveryPath;
        long recoveryGeneration = _recoveryGeneration;
        DocManager.SaveCheckpoint point = doc.CaptureSaveCheckpoint();
        Stopwatch total = Stopwatch.StartNew();
        UProject snapshot = ProjectSaveSnapshot.Capture(doc.Project, template);
        Log.Information("保存阶段 {Stage}：{Milliseconds:F2} ms，Auto={Auto}，Template={Template}",
            "Capture", total.Elapsed.TotalMilliseconds, autoSave, template);

        progress?.Report(ProjectSaveStage.Waiting);
        Stopwatch phase = Stopwatch.StartNew();
        await _writer.WaitAsync();
        try
        {
            LogPhase("Wait", phase);
            // 排队期间已经换工程的任务无需再消耗磁盘；已开始的写盘始终使用捕获路径。
            if (!doc.IsSaveCheckpointCurrent(point)) return false;
            await Task.Run(() => WriteSnapshot(snapshot, path, progress));
            progress?.Report(ProjectSaveStage.Synchronizing);
            phase.Restart();
            await ServiceHub.FlushFileSystemAsync();
            LogPhase("SyncProject", phase);
            if (!doc.IsSaveCheckpointCurrent(point)) return false;
            if (template) return true;

            progress?.Report(ProjectSaveStage.Confirming);
            if (autoSave)
            {
                // 用户丢弃恢复或其他事务已经发布恢复路径时，旧任务不得恢复该提示。
                if (_recoveryGeneration != recoveryGeneration || Preferences.Default.RecoveryPath != previousRecovery) return false;
                Preferences.Default.RecoveryPath = path;
            }
            else
            {
                Preferences.Default.RecoveryPath = string.Empty;
                Preferences.Default.LastSaveProjectDirectory = Path.GetDirectoryName(path) ?? string.Empty;
                // 已知目标刚刚成功写入，最近记录不再逐个同步查盘。
                var recent = Preferences.Default.RecentFiles;
                recent.RemoveAll(file => string.IsNullOrEmpty(file) || file == path ||
                    file.StartsWith(PathManager.Inst.TemplatesPath + Path.DirectorySeparatorChar, StringComparison.Ordinal));
                recent.Insert(0, path);
                if (recent.Count > 16) recent.RemoveRange(16, recent.Count - 16);
            }
            await PreferencesWriter.Inst.SaveAsync();
            LogPhase("Preferences", phase);
            await ServiceHub.FlushFileSystemAsync();
            LogPhase("SyncPreferences", phase);
            if (autoSave && (_recoveryGeneration != recoveryGeneration || Preferences.Default.RecoveryPath != path)) return false;
            bool committed = doc.CompleteSave(point, path, autoSave);
            Log.Information("保存完成：{Milliseconds:F2} ms，Auto={Auto}，Committed={Committed}",
                total.Elapsed.TotalMilliseconds, autoSave, committed);
            return committed;
        }
        finally { _writer.Release(); }
    }

    /// <summary>只访问独立快照；分开记录 CPU 序列化、文件写入和替换阶段。</summary>
    private static void WriteSnapshot(UProject snapshot, string path, IProgress<ProjectSaveStage>? progress)
    {
        Stopwatch phase = Stopwatch.StartNew();
        progress?.Report(ProjectSaveStage.Preparing);
        ProjectSaveSnapshot.Prepare(snapshot, path);
        LogPhase("Prepare", phase);
        progress?.Report(ProjectSaveStage.Serializing);
        string yaml = SaveSerializer.Value.Serialize(snapshot);
        LogPhase("Serialize", phase);
        progress?.Report(ProjectSaveStage.Writing);
        long bytes = AtomicFileWriter.Write(path, writer => writer.Write(yaml), () =>
        {
            LogPhase("Write", phase);
            progress?.Report(ProjectSaveStage.Replacing);
        });
        LogPhase("Replace", phase);
        Log.Information("工程写入 {Bytes} bytes：{Path}", bytes, path);
    }

    /// <summary>记录阶段耗时并开始下一阶段计时。</summary>
    private static void LogPhase(string stage, Stopwatch watch)
    {
        Log.Information("保存阶段 {Stage}：{Milliseconds:F2} ms", stage, watch.Elapsed.TotalMilliseconds);
        watch.Restart();
    }
}
