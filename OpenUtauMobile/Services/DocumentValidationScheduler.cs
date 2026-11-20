using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Threading;
using OpenUtau.Core;

namespace OpenUtauMobile.Services;

/// <summary>把文档校验合并到下一动画帧，并在停止出帧或宿主卸载时完成待处理校验。</summary>
internal sealed class DocumentValidationScheduler : IDisposable
{
    private readonly TopLevel _topLevel;
    private readonly Action<Action> _requestFrame;
    private readonly HashSet<Action> _pendingActions = [];
    private readonly DispatcherTimer _fallbackTimer;
    private int _generation;
    private bool _isDisposed;

    /// <summary>在 UI 线程为当前主视图接入 Core 已有的帧调度接口。</summary>
    public DocumentValidationScheduler(TopLevel topLevel)
    {
        Dispatcher.UIThread.VerifyAccess();
        _topLevel = topLevel;
        _requestFrame = RequestFrame;
        // 窗口隐藏或 Android 暂停出帧时也不能无限推迟校验；仅有待处理工作时启动。
        _fallbackTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _fallbackTimer.Tick += OnFallbackTick;
        DocManager.Inst.RequestFrame = _requestFrame;
    }

    /// <summary>合并同一帧内的重复冲刷请求，实际校验始终保留在 UI 线程。</summary>
    private void RequestFrame(Action action)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_isDisposed)
        {
            action();
            return;
        }

        _pendingActions.Add(action);
        if (_fallbackTimer.IsEnabled)
        {
            return;
        }

        int generation = ++_generation;
        _fallbackTimer.Start();
        _topLevel.RequestAnimationFrame(_ =>
        {
            // 旧宿主、超时兜底以及上一次冲刷遗留的帧回调不能提前执行新一批校验。
            if (!_isDisposed && generation == _generation)
            {
                Flush();
            }
        });
    }

    /// <summary>没有动画帧到来时，在下一次可执行的 UI 调度中完成校验。</summary>
    private void OnFallbackTick(object? sender, EventArgs e)
    {
        Flush();
    }

    /// <summary>先清理本批调度状态，再执行回调，允许校验过程中产生下一批请求。</summary>
    private void Flush()
    {
        _fallbackTimer.Stop();
        _generation++;
        Action[] actions = [.. _pendingActions];
        _pendingActions.Clear();
        foreach (Action action in actions)
        {
            action();
        }
    }

    /// <summary>卸载宿主时撤销接线并立即冲刷，避免遗留回调或丢失待校验状态。</summary>
    public void Dispose()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        if (DocManager.Inst.RequestFrame == _requestFrame)
        {
            DocManager.Inst.RequestFrame = null;
        }

        _fallbackTimer.Tick -= OnFallbackTick;
        Flush();
    }
}
