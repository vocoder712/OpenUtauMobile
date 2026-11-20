using System;
using OpenUtau.Core.Ustx;

namespace OpenUtauMobile.Services.Editor
{
    /// <summary>根据当前回放音符平滑调整钢琴卷帘的纵向视口。</summary>
    public sealed class PianoRollPlaybackFollow
    {
        private const double EdgeRatio = 0.1;
        private const double StopEpsilonKeys = 0.01;
        private const double LerpSharpness = 10;
        private const double MaxStepViewportRatio = 0.35;
        private UVoicePart? _part;
        private UNote? _targetNote;
        private int _mode;
        private double _visibleKeys;
        private double _target;
        private bool _active;

        /// <summary>清除旧音符与未完成的滚动目标。</summary>
        public void Reset()
        {
            _part = null;
            _targetNote = null;
            _active = false;
        }

        /// <summary>按播放位置确定纵向滚动目标，并用指数趋近插值推进一次动画。</summary>
        /// <remarks>
        /// 这不是固定时长的起终点补间：每次从当前偏移出发，移动剩余距离的一部分，越接近目标越慢。
        /// 未触发单步限幅时，剩余距离按 exp(-10 * seconds) 衰减，时间常数为 0.1 秒。
        /// 新音符触发滚动时只替换目标，下一步直接从当前偏移转向；不等待旧动画结束。
        /// </remarks>
        /// <param name="mode">0 为关闭，1 为边界滚动，2 为保持中心；其他值按关闭处理。</param>
        /// <param name="playing">是否正在实际回放；停止或等待渲染时清除动画。</param>
        /// <param name="inputActive">用户是否正在操作；为真时清除动画并保留用户控制的偏移。</param>
        /// <param name="part">钢琴卷帘当前编辑的人声分片。</param>
        /// <param name="tick">工程绝对播放 Tick，查找音符前转换为分片相对 Tick。</param>
        /// <param name="offset">当前视口顶部的纵向偏移，单位为半音，向下增大。</param>
        /// <param name="visibleKeys">扣除参数面板后可见的半音数量，可以包含小数。</param>
        /// <param name="seconds">本次动画的时间步长；调用方当前传入配置刷新率的倒数，并非实测帧间隔。</param>
        /// <returns>本次插值后的纵向偏移；未启动动画时返回原偏移。</returns>
        public double Update(int mode, bool playing, bool inputActive, UVoicePart? part, int tick,
            double offset, double visibleKeys, double seconds)
        {
            // 关闭、停止、用户操作或视口无效时，旧目标一并清除，后续不能继续执行过期动画。
            if (mode is not (1 or 2) || !playing || inputActive || part == null
                || !double.IsFinite(visibleKeys) || visibleKeys <= 0)
            {
                Reset();
                return offset;
            }

            // 分片、模式或可见高度改变后，旧目标的依据已失效；本次从当前偏移重新判断目标。
            if (_part != part || _mode != mode || _visibleKeys != visibleKeys)
            {
                Reset();
                _part = part;
                _mode = mode;
                _visibleKeys = visibleKeys;
            }

            // 空位置不产生新目标，已有滚动继续完成，避免短休止截断动画。
            UNote? note = FindPlayingNote(part, tick - part.position);
            if (note != null)
            {
                // key 是音符中心在完整音域中从上向下的坐标；y 是其相对视口顶部的位置，均以半音计。
                double key = ViewConstants.MaxTone - 0.5 - note.AdjustedTone;
                double y = key - offset;
                // 中心模式始终有目标；边界模式仅在音符上沿或下沿越界时触发，半个音符高度为 0.5。
                if (mode == 2 || y - 0.5 < 0 || y + 0.5 > visibleKeys)
                {
                    // 同一音符的动画保留原目标，回到视口内也继续完成。
                    // 新音符必须满足上面的触发条件才替换旧目标；在视口内不会打断边界模式的旧动画。
                    if (!_active || _targetNote != note)
                    {
                        // 音符中心目标为顶部 10%、底部内侧 10%（即顶部 90%），或中央 50%。
                        // 由 y = key - offset 反解目标偏移；接近音域两端时以合法滚动范围为准。
                        double ratio = mode == 2 ? 0.5 : y < visibleKeys * 0.5 ? EdgeRatio : 1 - EdgeRatio;
                        _target = Math.Clamp(key - visibleKeys * ratio, 0, Math.Max(0, ViewConstants.MaxTone - visibleKeys));
                        _targetNote = note;
                        _active = true;
                    }
                }
            }

            // 没有待完成的目标则保持原位；有目标时，不依赖当前音符是否仍越界或是否进入休止。
            if (!_active) return offset;
            double delta = _target - offset;
            // 指数趋近理论上不会精确到达目标，剩余距离不超过 0.01 半音时直接对齐并结束。
            if (Math.Abs(delta) <= StopEpsilonKeys)
            {
                _active = false;
                return _target;
            }

            // 每次取剩余距离的 alpha 比例：next = offset + (target - offset) * alpha。
            // 默认 20 Hz 时 seconds = 0.05、alpha ≈ 0.393，即每次移动约 39.3% 的剩余距离。
            // 负时间按零处理；改变时间步长会相应调整比例，未限幅时保持相同的指数衰减速度。
            double alpha = 1 - Math.Exp(-LerpSharpness * Math.Max(0, seconds));
            // 大跨度跳音时，单次位移最多为可见高度的 35%，避免一次更新跨越过大。
            // 此限幅会延长大跨度滚动的收敛时间；它按更新次数生效，并非每秒速度上限。
            double step = Math.Clamp(delta * alpha, -visibleKeys * MaxStepViewportRatio, visibleKeys * MaxStepViewportRatio);
            double next = offset + step;
            // 本次推进后再检查一次阈值，达到精度就立即落到目标，避免多等一次更新。
            if (Math.Abs(_target - next) <= StopEpsilonKeys)
            {
                _active = false;
                return _target;
            }
            return next;
        }

        /// <summary>按音符集合顺序取覆盖当前 Tick 的首个有效音符，结束 Tick 属于下一段。</summary>
        private static UNote? FindPlayingNote(UVoicePart part, int tick)
        {
            if (tick < 0 || tick >= part.Duration) return null;
            foreach (UNote? note in part.notes)
            {
                if (note == null) continue;
                if (note.position > tick) break;
                if (!note.Error && tick < note.End) return note;
            }
            return null;
        }
    }
}
