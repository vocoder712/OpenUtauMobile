using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;

namespace OpenUtauMobile.Browser.Services.Audio;

/// <summary>固定在 WASM 共享内存中的单生产者、单消费者音频队列。</summary>
internal sealed class BrowserAudioBuffer
{
    // 与 audio-output-buffer.js 共用布局；计数均为立体声帧，允许无符号回绕。
    private const int Read = 0, Write = 1, Playing = 2, Ended = 3, Consumed = 4, Running = 5;
    private const int HeaderLength = 8;
    internal const int Capacity = 4096;
    internal const int TargetFrames = 2048;
    private static readonly ConcurrentDictionary<int, BrowserAudioBuffer> retained = new();
    private readonly int[] storage = new int[HeaderLength + Capacity * 2];
    private readonly GCHandle pin;
    private uint lastConsumed;
    private long consumedFrames;

    /// <summary>固定队列地址，内存增长和托管回收均不会移动音频数据。</summary>
    internal BrowserAudioBuffer() => pin = GCHandle.Alloc(storage, GCHandleType.Pinned);

    /// <summary>浏览器放弃引用且后台生产结束之后，才允许解除固定。</summary>
    ~BrowserAudioBuffer()
    {
        if (pin.IsAllocated) pin.Free();
    }

    /// <summary>发布给浏览器的队列必须保活，直到音频线程确认已切换。</summary>
    internal int Retain(int generation)
    {
        retained[generation] = this;
        return pin.AddrOfPinnedObject().ToInt32();
    }

    /// <summary>音频线程确认旧队列不再使用后，移除浏览器持有的引用。</summary>
    internal static void Release(int generation) => retained.TryRemove(generation, out _);

    /// <summary>直接发布播放意图，不依赖主线程及时处理控制消息。</summary>
    internal bool IsPlaying
    {
        get => Volatile.Read(ref storage[Playing]) != 0;
        set => Volatile.Write(ref storage[Playing], value ? 1 : 0);
    }

    /// <summary>浏览器主线程只在上下文状态变化时更新运行标记。</summary>
    internal bool IsRunning => Volatile.Read(ref storage[Running]) != 0;

    /// <summary>设备尚未消费的帧数；写指针只能在样本复制完成后发布。</summary>
    internal int QueuedFrames => unchecked((int)((uint)Volatile.Read(ref storage[Write]) -
        (uint)Volatile.Read(ref storage[Read])));

    /// <summary>读取消费时钟并展开回绕；调用者须串行访问。</summary>
    internal long ConsumedFrames
    {
        get
        {
            uint current = unchecked((uint)Volatile.Read(ref storage[Consumed]));
            consumedFrames += unchecked(current - lastConsumed);
            lastConsumed = current;
            return consumedFrames;
        }
    }

    /// <summary>生产结束只发布标记，音频线程自行排空剩余样本。</summary>
    internal void EndInput() => Volatile.Write(ref storage[Ended], 1);

    /// <summary>向已预留的空位写入交错样本，跨越环尾时分两段复制。</summary>
    internal void WriteSamples(ReadOnlySpan<float> samples)
    {
        uint write = unchecked((uint)Volatile.Read(ref storage[Write]));
        int offset = (int)(write % Capacity) * 2;
        Span<float> destination = MemoryMarshal.Cast<int, float>(storage.AsSpan(HeaderLength));
        int first = Math.Min(samples.Length, destination.Length - offset);
        samples[..first].CopyTo(destination[offset..]);
        samples[first..].CopyTo(destination);
        Volatile.Write(ref storage[Write], unchecked((int)(write + (uint)(samples.Length / 2))));
    }
}
