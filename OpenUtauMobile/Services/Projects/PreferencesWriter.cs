using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Threading;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtauMobile.Services.Projects;

/// <summary>宿主偏好单写入器；UI 只复制内存，后台合并并原子写入，关键事务可等待持久化。</summary>
public sealed class PreferencesWriter
{
    /// <summary>应用进程唯一的偏好写入器，平台生命周期通过此实例请求刷新。</summary>
    public static PreferencesWriter Inst { get; } = new();
    private static readonly ConcurrentDictionary<Type, (FieldInfo[] Fields, PropertyInfo[] Properties)> Members = new();
    private readonly List<(long Version, TaskCompletionSource Completion)> _waiters = [];
    private PendingSave? _pending;
    private Task? _pump;
    private long _version;
    private long _completedVersion;
    private string? _lastWrittenJson;
    private string? _lastWrittenPath;

    /// <summary>在应用启动时注册；未注册宿主的上游程序仍使用原同步实现。</summary>
    public void Install() => Preferences.SaveOverride = RequestSave;

    /// <summary>统一将请求派发到 UI，避免后台枚举可变偏好集合。</summary>
    private void RequestSave()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RequestSave);
            return;
        }
        var snapshot = (Preferences.SerializablePreferences)CopyValue(Preferences.Default)!;
        _pending = new(++_version, snapshot, PathManager.Inst.PrefsFilePath);
        _pump ??= PumpAsync();
    }

    /// <summary>关键事务直接请求并等待持久化，捕获失败也向调用者传播。</summary>
    public Task SaveAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        RequestSave();
        return FlushAsync();
    }

    /// <summary>等待调用时已请求的偏好版本持久化；失败向调用者传播，下一请求可重试。</summary>
    public Task FlushAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_completedVersion == _version) return Task.CompletedTask;
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiters.Add((_version, completion));
        _pump ??= PumpAsync();
        return completion.Task;
    }

    /// <summary>偏好数据仅含公开 DTO 字段/可写属性；复制集合及嵌套 DTO，不序列化活动对象。</summary>
    private static object? CopyValue(object? value)
    {
        if (value == null || value is string || value.GetType().IsValueType) return value;
        Type type = value.GetType();
        if (value is Array array)
        {
            Array copy = Array.CreateInstance(type.GetElementType()!, array.Length);
            for (int i = 0; i < array.Length; i++) copy.SetValue(CopyValue(array.GetValue(i)), i);
            return copy;
        }
        if (value is IDictionary dictionary)
        {
            IDictionary copy = (IDictionary)Activator.CreateInstance(type)!;
            foreach (DictionaryEntry entry in dictionary) copy.Add(entry.Key, CopyValue(entry.Value));
            return copy;
        }
        if (value is IList list)
        {
            IList copy = (IList)Activator.CreateInstance(type)!;
            foreach (object? item in list) copy.Add(CopyValue(item));
            return copy;
        }
        object result = Activator.CreateInstance(type)!;
        var members = Members.GetOrAdd(type, current => (
            [.. current.GetFields(BindingFlags.Instance | BindingFlags.Public).Where(field => !field.IsInitOnly)],
            [.. current.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(property => property.CanRead && property.SetMethod?.IsPublic == true && property.GetIndexParameters().Length == 0)]));
        foreach (FieldInfo field in members.Fields) field.SetValue(result, CopyValue(field.GetValue(value)));
        foreach (PropertyInfo property in members.Properties) property.SetValue(result, CopyValue(property.GetValue(value)));
        return result;
    }

    /// <summary>每批只保留最新偏好快照，避免连续操作反复写入闪存。</summary>
    private async Task PumpAsync()
    {
        await Task.Delay(200);
        try
        {
            while (_pending is { } pending)
            {
                _pending = null;
                try
                {
                    await Task.Run(() =>
                    {
                        string json = Json.Serialize(pending.Snapshot, Json.WriteIndentedOptions);
                        // 自动保存通常只重复同一个恢复路径；数据未变时不再写偏好文件。
                        if (json == _lastWrittenJson && pending.Path == _lastWrittenPath && File.Exists(pending.Path)) return;
                        AtomicFileWriter.Write(pending.Path, writer => writer.Write(json));
                        _lastWrittenJson = json;
                        _lastWrittenPath = pending.Path;
                    });
                    _completedVersion = pending.Version;
                    FinishWaiters(pending.Version, null);
                }
                catch (Exception error)
                {
                    _pending ??= pending;
                    Log.Error(error, "偏好后台保存失败");
                    FinishWaiters(long.MaxValue, error);
                    break;
                }
            }
        }
        finally { _pump = null; }
    }

    /// <summary>完成已落盘版本的等待者；失败保留最新待写快照供后续重试。</summary>
    private void FinishWaiters(long version, Exception? error)
    {
        for (int i = _waiters.Count - 1; i >= 0; i--)
        {
            if (_waiters[i].Version > version) continue;
            TaskCompletionSource completion = _waiters[i].Completion;
            _waiters.RemoveAt(i);
            if (error == null) completion.TrySetResult();
            else completion.TrySetException(error);
        }
    }

    private sealed record PendingSave(long Version, Preferences.SerializablePreferences Snapshot, string Path);
}
