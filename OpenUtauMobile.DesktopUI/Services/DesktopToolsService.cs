using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.Util;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.ViewModels;
using Serilog;

namespace OpenUtauMobile.DesktopUI.Services
{
    public enum DesktopToolKind { Resampler, Wavtool }
    public sealed record DesktopTool(string Name, string? FilePath, DesktopToolKind Kind, bool Builtin, string Status, bool Available, string? StatusKey = null)
    {
        public override string ToString() => Name;
    }

    /// <summary>桌面工具只使用既有引擎；项目结束后串行更新工具注册表。</summary>
    public sealed class DesktopToolsService
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        public static DesktopToolsService Instance { get; } = new();
        public event Action? Changed;
        public bool Pending { get; private set; }
        public async Task WaitForIdleAsync()
        {
            await _gate.WaitAsync();
            _gate.Release();
        }

        public static string Folder(DesktopToolKind kind) => kind == DesktopToolKind.Resampler ? PathManager.Inst.ResamplersPath : PathManager.Inst.WavtoolsPath;
        public static IReadOnlyList<DesktopTool> GetTools(DesktopToolKind kind)
        {
            List<DesktopTool> tools = [];
            IEnumerable<string> registered = kind == DesktopToolKind.Resampler
                ? ToolsManager.Inst.Resamplers.Select(r => r.ToString()!)
                : ToolsManager.Inst.Wavtools.Select(w => w.ToString()!);
            string folder = Folder(kind);
            foreach (string name in registered)
            {
                string path = Path.Combine(folder, name);
                bool builtin = kind == DesktopToolKind.Resampler ? name == "worldline" : name == SharpWavtool.nameConvergence || name == SharpWavtool.nameSimple;
                if (builtin) tools.Add(new(name, null, kind, true, L.S("Desktop.Builtin"), true, "Desktop.Builtin"));
                else if (!File.Exists(path)) tools.Add(new(name, path, kind, false, L.S("Desktop.Unavailable"), false, "Desktop.Unavailable"));
            }
            if (!Directory.Exists(folder)) return tools;
            string[] files;
            try
            {
                files = Directory.EnumerateFiles(folder, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = false,
                    AttributesToSkip = FileAttributes.ReparsePoint
                }).Where(IsToolFile).ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                tools.Add(new(Path.GetFileName(folder), folder, kind, false, exception.ToString(), false));
                return tools;
            }
            foreach (string file in files)
            {
                bool windowsTool = Path.GetExtension(file).ToLowerInvariant() is ".exe" or ".bat";
                bool nativeWavtool = kind == DesktopToolKind.Wavtool && !OperatingSystem.IsWindows() && !windowsTool;
                bool compatible = OperatingSystem.IsWindows() ? windowsTool : !nativeWavtool && (!windowsTool || IsExecutable(Preferences.Default.WinePath));
                bool permission = windowsTool || OperatingSystem.IsWindows() || IsExecutable(file);
                compatible &= permission;
                string status = !permission ? "Desktop.NotExecutable" : nativeWavtool ? "Desktop.NativeWavtoolUnavailable" : !compatible ? "Desktop.Unavailable" : windowsTool && !OperatingSystem.IsWindows() ? "Desktop.ExperimentalWine" : "Desktop.Discovered";
                tools.Add(new(Path.GetRelativePath(folder, file), file, kind, false, L.S(status), compatible, status));
            }
            return tools;
        }
        public static bool IsExecutable(string path)
        {
            if (!File.Exists(path)) return false;
            if (OperatingSystem.IsWindows()) return true;
            return (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        private static bool IsToolFile(string file) => Path.GetExtension(file).ToLowerInvariant() is ".exe" or ".bat" or ".sh" or "";
        public static bool IsInUse(DesktopTool tool)
        {
            if (tool.Builtin) return true;
            UProject project = DocManager.Inst.Project;
            if (project.tracks.Any(t => (tool.Kind == DesktopToolKind.Resampler ? t.RendererSettings.resampler : t.RendererSettings.wavtool) == tool.Name)) return true;
            if (tool.Kind != DesktopToolKind.Resampler) return false;
            foreach (UVoicePart part in project.parts.OfType<UVoicePart>())
            {
                if (part.trackNo < 0 || part.trackNo >= project.tracks.Count) continue;
                UTrack track = project.tracks[part.trackNo];
                if (!track.TryGetExpDescriptor(project, OpenUtau.Core.Format.Ustx.ENG, out UExpressionDescriptor descriptor)) continue;
                foreach (UExpression expression in part.notes.SelectMany(n => n.phonemeExpressions).Where(e => e.abbr == OpenUtau.Core.Format.Ustx.ENG))
                {
                    int index = (int)expression.value;
                    if (index >= 0 && index < descriptor.options.Length && descriptor.options[index] == tool.Name) return true;
                }
                foreach (UPhoneme phoneme in part.phonemes)
                {
                    int index = (int)phoneme.GetExpression(project, track, OpenUtau.Core.Format.Ustx.ENG).Item1;
                    if (index >= 0 && index < descriptor.options.Length && descriptor.options[index] == tool.Name) return true;
                }
            }
            return false;
        }
        public async Task<bool> ApplyAsync(MainViewModel main, Func<CancellationToken, Task<bool>> action, CancellationToken cancellation)
        {
            await _gate.WaitAsync(cancellation);
            try
            {
                Pending = main.ActiveEditor != null;
                Changed?.Invoke();
                while (main.ActiveEditor != null) await Task.Delay(200, cancellation);
                cancellation.ThrowIfCancellationRequested();
                bool changed = await action(cancellation);
                if (!changed) return false;
                // 不并行搜索两个列表，避免渲染器读到半更新的映射。
                await Task.Run(() => ToolsManager.Inst.Initialize());
                return true;
            }
            finally { Pending = false; _gate.Release(); Changed?.Invoke(); }
        }
        public static async Task<bool> InstallAsync(string source, DesktopToolKind kind, bool folder, CancellationToken cancellation)
        {
            if (folder ? !Directory.Exists(source) : !File.Exists(source) || !IsToolFile(source))
                throw new IOException(L.S(folder ? "Desktop.InvalidToolFolder" : "Desktop.InvalidToolFile"));
            string root = Path.GetFullPath(Folder(kind));
            Directory.CreateDirectory(root);
            string sourceRoot = Path.GetFullPath(folder ? source : Path.GetDirectoryName(source)!);
            string destination = folder ? Path.Combine(root, Path.GetFileName(sourceRoot.TrimEnd(Path.DirectorySeparatorChar))) : root;
            EnsureChild(root, destination, allowRoot: true);
            if (folder && (destination.Equals(sourceRoot, StringComparison.OrdinalIgnoreCase) || destination.StartsWith(sourceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                throw new IOException(L.S("Desktop.InvalidToolFolder"));
            // 单文件安装只复制所选程序和引擎识别的旁置配置；完整依赖包请用目录安装。
            if ((File.GetAttributes(sourceRoot) & FileAttributes.ReparsePoint) != 0) throw new IOException(L.S("Desktop.InvalidToolFolder"));
            (string[] files, string[] collisions) = await Task.Run(() =>
            {
                cancellation.ThrowIfCancellationRequested();
                StringComparison pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                string sourcePath = Path.GetFullPath(source);
                string basename = Path.GetFileNameWithoutExtension(source);
                string manifestPath = Path.ChangeExtension(sourcePath, ".yaml");
                string moreConfigPath = Path.Combine(sourceRoot, "moreconfig.txt");
                string[] packageFiles = EnumeratePackageFiles(sourceRoot, folder, cancellation).Where(file => folder
                    || Path.GetFullPath(file).Equals(sourcePath, pathComparison)
                    || Path.GetFullPath(file).Equals(manifestPath, pathComparison)
                    || basename.Equals("moresampler", StringComparison.OrdinalIgnoreCase) && Path.GetFullPath(file).Equals(moreConfigPath, pathComparison)).ToArray();
                packageFiles = packageFiles.Where(file => !Path.GetFullPath(Path.Combine(destination, Path.GetRelativePath(sourceRoot, file)))
                    .Equals(Path.GetFullPath(file), pathComparison)).ToArray();
                List<string> existingFiles = [];
                foreach (string file in packageFiles)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException(L.S("Desktop.InvalidToolFolder"));
                    string target = Path.GetFullPath(Path.Combine(destination, Path.GetRelativePath(sourceRoot, file)));
                    EnsureChild(root, target);
                    if (File.Exists(target)) existingFiles.Add(target);
                }
                return (packageFiles, existingFiles.ToArray());
            }, cancellation);
            if (files.Length == 0) return false;
            if (collisions.Length > 0)
            {
                string? choice = await OptionConfirmPopupService.ShowAsync(L.S("Desktop.ReplaceTool"), string.Join(Environment.NewLine, collisions),
                    new[] { new OptionConfirmOption(L.S("Common.Cancel"), "cancel", isDefault: true), new OptionConfirmOption(L.S("Desktop.Replace"), "replace", isDestructive: true) });
                if (choice != "replace") return false;
            }
            cancellation.ThrowIfCancellationRequested();
            string stagingParent = Path.GetDirectoryName(root) ?? root;
            string staging = Path.Combine(stagingParent, ".tool-install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                await Task.Run(() =>
                {
                    byte[] buffer = new byte[128 * 1024];
                    foreach (string file in files)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        string staged = Path.Combine(staging, Path.GetRelativePath(sourceRoot, file));
                        Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
                        using (FileStream input = new(file, FileMode.Open, FileAccess.Read, FileShare.Read, buffer.Length, FileOptions.SequentialScan))
                        using (FileStream output = new(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, FileOptions.SequentialScan))
                        {
                            int read;
                            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                cancellation.ThrowIfCancellationRequested();
                                output.Write(buffer, 0, read);
                            }
                        }
                        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(staged, File.GetUnixFileMode(file));
                    }
                }, cancellation);
                cancellation.ThrowIfCancellationRequested();
                await Task.Run(() =>
                {
                    foreach (string file in files)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        string target = Path.GetFullPath(Path.Combine(destination, Path.GetRelativePath(sourceRoot, file)));
                        EnsureChild(root, target);
                        if (Directory.Exists(target)) throw new IOException($"A directory already occupies the tool file path: {target}");
                        if (File.Exists(target))
                        {
                            if (File.GetAttributes(target).HasFlag(FileAttributes.ReadOnly)) throw new UnauthorizedAccessException($"The tool file is read-only: {target}");
                            using FileStream _ = File.Open(target, FileMode.Open, FileAccess.Write, FileShare.None);
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    }
                    cancellation.ThrowIfCancellationRequested();

                    List<(string target, string? backup)> published = [];
                    try
                    {
                        foreach (string file in files)
                        {
                            string target = Path.GetFullPath(Path.Combine(destination, Path.GetRelativePath(sourceRoot, file)));
                            StringComparison pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                            if (file.Equals(target, pathComparison)) continue;
                            string staged = Path.Combine(staging, Path.GetRelativePath(sourceRoot, file));
                            string? backup = null;
                            if (File.Exists(target))
                            {
                                backup = Path.Combine(staging, ".previous", Path.GetRelativePath(destination, target));
                                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                                File.Move(target, backup);
                            }
                            published.Add((target, backup));
                            File.Move(staged, target, true);
                        }
                    }
                    catch
                    {
                        for (int index = published.Count - 1; index >= 0; index--)
                        {
                            (string target, string? backup) = published[index];
                            try
                            {
                                if (File.Exists(target)) File.Delete(target);
                                if (backup != null && File.Exists(backup)) File.Move(backup, target);
                            }
                            catch (Exception rollbackError) when (rollbackError is IOException or UnauthorizedAccessException)
                            {
                                Log.Error(rollbackError, "Failed to restore tool file {Path} after an incomplete install", target);
                            }
                        }
                        throw;
                    }
                });
                return true;
            }
            finally
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                { Log.Warning(exception, "Failed to remove staged tool package {Path}", staging); }
            }
        }
        public static void Uninstall(DesktopTool tool)
        {
            if (tool.Builtin || tool.FilePath == null || IsInUse(tool)) throw new InvalidOperationException(L.S("Desktop.ToolInUse"));
            string root = Path.GetFullPath(Folder(tool.Kind));
            string path = Path.GetFullPath(tool.FilePath);
            EnsureChild(root, path);
            File.Delete(path);
            string manifest = Path.ChangeExtension(path, ".yaml");
            if (File.Exists(manifest)) File.Delete(manifest);
        }
        private static void EnsureChild(string root, string path, bool allowRoot = false)
        {
            string full = Path.GetFullPath(path);
            if (File.Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) throw new IOException(L.S("Desktop.InvalidToolFolder"));
            if (!(allowRoot && full.Equals(root, StringComparison.OrdinalIgnoreCase)) && !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException(L.S("Desktop.InvalidToolFolder"));
            for (string? current = Path.GetDirectoryName(full); current != null && current.Length >= root.Length; current = Path.GetDirectoryName(current))
                if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException(L.S("Desktop.InvalidToolFolder"));
        }
        private static IEnumerable<string> EnumeratePackageFiles(string root, bool recursive, CancellationToken cancellation)
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(root))
            {
                cancellation.ThrowIfCancellationRequested();
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException(L.S("Desktop.InvalidToolFolder"));
                if ((attributes & FileAttributes.Directory) == 0) yield return entry;
                else if (recursive) foreach (string file in EnumeratePackageFiles(entry, true, cancellation)) yield return file;
            }
        }
    }
}
