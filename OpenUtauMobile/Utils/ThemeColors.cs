using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenUtauMobile.Utils
{
    /// <summary>
    /// 应用程序主题颜色类
    /// </summary>
    public abstract class ThemeColors
    {
        public abstract SKColor Primary { get; set; }
        #region 钢琴键
        /// <summary>
        /// 钢琴卷帘左侧白键
        /// </summary>
        public abstract SKColor WhitePianoKey {  get; set; }
        /// <summary>
        /// 钢琴卷帘左侧黑键
        /// </summary>
        public abstract SKColor BlackPianoKey {  get; set; }
        /// <summary>
        /// 钢琴卷帘左侧白键文本
        /// </summary>
        public abstract SKColor WhitePianoKeyText { get; set; }
        /// <summary>
        /// 钢琴卷帘左侧黑键文本
        /// </summary>
        public abstract SKColor BlackPianoKeyText { get; set; }
        #endregion
        #region 钢琴卷帘
        /// <summary>
        /// 黑键钢琴卷帘背景
        /// </summary>
        public abstract SKColor BlackPianoRollBackground { get; set; }
        /// <summary>
        /// 白键钢琴卷帘背景
        /// </summary>
        public abstract SKColor WhitePianoRollBackground { get; set; }
        /// <summary>
        /// 钢琴卷帘小节线
        /// </summary>
        public abstract SKPaint PianoRollBarlinePaint { get; set; }
        /// <summary>
        /// 钢琴卷帘节拍线
        /// </summary>
        public abstract SKPaint PianoRollBeatlinePaint { get; set; }
        ///// <summary>
        ///// 钢琴卷帘非小节虚线线
        ///// </summary>
        //public abstract SKPaint PianoRollDashedlinePaint { get; set; }
        /// <summary>
        /// 钢琴卷帘小节线头部
        /// </summary>
        public abstract SKPaint PianoRollBarlineHeadPaint { get; set; }
        /// <summary>
        /// 钢琴卷帘节拍线头部
        /// </summary>
        public abstract SKPaint PianoRollBeatlineHeadPaint { get; set; }
        /// <summary>
        /// 钢琴卷帘播放位置线画笔
        /// </summary>
        public abstract SKPaint PianoRollPlaybackPosLinePaint { get; set; }
        /// <summary>
        /// 钢琴卷帘歌词文本颜色
        /// </summary>
        public abstract SKColor LyricsText { get; set; }
        /// <summary>
        /// 选中的音符边框颜色
        /// </summary>
        public abstract SKColor SelectedNoteBorder { get; set; }
        /// <summary>
        /// 音高线
        /// </summary>
        public abstract SKColor PitchLine { get; set; }
        /// <summary>
        /// 钢琴卷帘分片以外区域阴影
        /// </summary>
        public abstract SKColor PianoRollShadow { get; set; }
        #endregion
        #region 弹出窗口
        ///// <summary>
        ///// 弹出窗口标题栏背景
        ///// </summary>
        //public abstract SKColor PopupTitleBackground { get; set; }
        #endregion
        #region 走带画布
        /// <summary>
        /// 走带画布背景
        /// </summary>
        public abstract SKColor TrackBackground { get; set; }
        /// <summary>
        /// 走带时间线
        /// </summary>
        public abstract SKColor TimeLine { get; set; }
        /// <summary>
        /// 节拍数字
        /// </summary>
        public abstract SKColor BarNumber { get; set; }
        /// <summary>
        /// 曲速标记文本
        /// </summary>
        public abstract SKColor TempoSignatureText { get; set; }
        /// <summary>
        /// 节拍标记文本
        /// </summary>
        public abstract SKColor TimeSignatureText { get; set; }
        /// <summary>
        /// 走带分片里面的音符
        /// </summary>
        public abstract SKColor TrackNote { get; set; }
        /// <summary>
        /// 选中的分片边框
        /// </summary>
        public abstract SKColor SelectedPartBorder { get; set; }
        /// <summary>
        /// 走带时间轴背景
        /// </summary>
        public abstract SKColor TimeLineBackground { get; set; }
        public abstract SKPaint TrackHorizontalLinePaint { get; set; }
        /// <summary>
        /// 走带标签颜色
        /// </summary>
        public abstract SKColor PartLabel { get; set; }
        #endregion
        #region 分隔栏
        /// <summary>
        /// 激活的编辑模式按钮
        /// </summary>
        public abstract SKColor ActiveNoteEditModeButton { get; set; }
        #endregion
        #region 音素画布
        public abstract SKColor PhonemePosLine { get; set; }
        public abstract SKPaint PhonemeTextPaint { get; set; }
        #endregion
        #region 表情画布
        /// <summary>
        /// 表情选项文本画笔
        /// </summary>
        public abstract SKPaint ExpressionOptionTextPaint { get; set; }
        /// <summary>
        /// 表情选项框画笔
        /// </summary>
        public abstract SKPaint ExpressionOptionBoxPaint { get; set; }
        public abstract SKPaint DefaultExpressionStrokePaint { get; set; }
        public abstract SKPaint EditedExpressionStrokePaint { get; set; }
        public abstract SKPaint DefaultExpressionFillPaint { get; set; }
        public abstract SKPaint EditedExpressionFillPaint { get; set; }
        #endregion
        #region 其它
        /// <summary>
        /// 正在绘制的光标
        /// </summary>
        public abstract SKPaint DrawingCursorPaint { get; set; }
        #endregion
    }

    public class LightThemeColors : ThemeColors
    {
        #region 钢琴键
        public override SKColor WhitePianoKey { get; set; } = SKColor.Parse("#FFFFFF");
        public override SKColor BlackPianoKey { get; set; } = SKColor.Parse("#fe71a3");
        public override SKColor WhitePianoKeyText { get; set; } = SKColor.Parse("#404040");
        public override SKColor BlackPianoKeyText { get; set; } = SKColor.Parse("#FFFFFF");
        #endregion
        public override SKColor BlackPianoRollBackground { get; set; } = SKColor.Parse("#f0f0f0");
        public override SKColor WhitePianoRollBackground { get; set; } = SKColor.Parse("#ffffff");
        //public override SKColor PopupTitleBackground { get; set; } = SKColor.Parse("#E0E0E0");
        public override SKColor TrackBackground { get; set; } = SKColor.Parse("#ffffff");
        public override SKColor TimeLine { get; set; } = SKColor.Parse("#000000");
        public override SKColor BarNumber { get; set; } = SKColor.Parse("#000000");
        public override SKPaint PianoRollBarlinePaint { get; set; } = new SKPaint
        {
            Color = SKColor.Parse("#b0000000"),
            StrokeWidth = 2,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };
        public override SKPaint PianoRollBeatlinePaint { get; set; } = new SKPaint
        {
            Color = SKColor.Parse("#80000000"),
            StrokeWidth = 1,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };
        public override SKPaint PianoRollBarlineHeadPaint { get; set; } = new SKPaint
        {
            Color = SKColor.Parse("#b0000000"),
            StrokeWidth = 4,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };
        public override SKPaint PianoRollBeatlineHeadPaint { get; set; } = new SKPaint
        {
            Color = SKColor.Parse("#80000000"),
            StrokeWidth = 2,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };
        public override SKColor TempoSignatureText { get; set; } = SKColor.Parse("#000000");
        public override SKColor TimeSignatureText { get; set; } = SKColor.Parse("#000000");
        public override SKColor TrackNote { get; set; } = SKColor.Parse("#000000");
        public override SKColor ActiveNoteEditModeButton { get; set; } = SKColor.Parse("#fe71a3");
        public override SKColor SelectedPartBorder { get; set; } = SKColor.Parse("#505050");
        public override SKColor TimeLineBackground { get; set; } = SKColor.Parse("#e0e0e0");
        public override SKPaint PianoRollPlaybackPosLinePaint { get; set; } = new SKPaint
        {
            Color = SKColor.Parse("#e0000000"),
            StrokeWidth = 3,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };
        public override SKColor LyricsText { get; set; } = SKColor.Parse("#000000");
        public override SKColor SelectedNoteBorder { get; set; } = SKColor.Parse("#606060");
        public override SKColor PhonemePosLine { get; set; } = SKColor.Parse("#000000");
        public override SKPaint PhonemeTextPaint { get; set; } = new SKPaint
        {
            Color = SKColor.Parse("#000000"),
            Style = SKPaintStyle.Fill,
        };
        public override SKColor PitchLine { get; set; } = SKColor.Parse("#80000000");
        public override SKPaint TrackHorizontalLinePaint { get; set; } = new SKPaint
        {
            Color = SKColor.Parse("#a0303030"),
            StrokeWidth = 2,
            Style = SKPaintStyle.Fill
        };
        public override SKColor PianoRollShadow { get; set; } = SKColor.Parse("#f0f0f0");
        public override SKColor PartLabel { get; set; } = SKColor.Parse("#000000");
        public override SKColor Primary { get; set; } = SKColor.Parse("#fe71a3");
        public override SKPaint ExpressionOptionTextPaint { get; set; } = new SKPaint
        {
            Color = SKColor.Parse("#000000"),
            Style = SKPaintStyle.Fill,
        };
        public override SKPaint ExpressionOptionBoxPaint { get; set; } = new SKPaint
        {
            Color = SKColor.Parse("#000000").WithAlpha(100),
            Style = SKPaintStyle.Fill,
            StrokeWidth = 2,
        };
        public override SKPaint DrawingCursorPaint { get; set; } = new()
        {
            Color = SKColors.Yellow,
            StrokeWidth = 2,
            IsAntialias = false,
            Style = SKPaintStyle.Stroke,
        };
        public override SKPaint DefaultExpressionStrokePaint { get; set; } = new()
        {
            StrokeWidth = 2,
            Color = SKColors.Gray,
            Style = SKPaintStyle.Stroke
        };
        public override SKPaint EditedExpressionStrokePaint { get; set; } = new()
        {
            StrokeWidth = 4,
            Color = SKColor.Parse("#fe71a3"),
            Style = SKPaintStyle.Stroke
        };
        public override SKPaint DefaultExpressionFillPaint { get; set; } = new()
        {
            Style = SKPaintStyle.Fill,
            Color = SKColors.LightGray.WithAlpha(100),
        };
        public override SKPaint EditedExpressionFillPaint { get; set; } = new()
        {
            Style = SKPaintStyle.Fill,
            Color = SKColor.Parse("#fe71a3").WithAlpha(150),
        };
    }
    public class DarkThemeColors : ThemeColors
    {
        #region 钢琴键
        public override SKColor WhitePianoKey { get; set; } = SKColor.Parse("#CC2A63");
        public override SKColor BlackPianoKey { get; set; } = SKColor.Parse("#00000000"); // 透明
        public override SKColor WhitePianoKeyText { get; set; } = SKColor.Parse("#FFFFFF");
        public override SKColor BlackPianoKeyText { get; set; } = SKColor.Parse("#FFFFFF");
        #endregion
        public override SKColor BlackPianoRollBackground { get; set; } = SKColor.Parse("#303030");
        public override SKColor WhitePianoRollBackground { get; set; } = SKColor.Parse("#3C3C3C");
        //public override SKColor PopupTitleBackground { get; set; } = SKColor.Parse("#505050");
        public override SKColor TrackBackground { get; set; } = SKColor.Parse("#2C2C2C");
        public override SKColor TimeLine { get; set; } = SKColor.Parse("#000000");
        public override SKColor BarNumber { get; set; } = SKColor.Parse("#FFFFFF");
        public override SKPaint PianoRollBarlinePaint { get; set; } = new SKPaint
        {
            Color = SKColor.Parse("#d0000000"),
            StrokeWidth = 2f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };
        public override SKPaint PianoRollBeatlinePaint { get; set; } = new SKPaint
        {
            Color = SKColor.Parse("#80000000"),
            StrokeWidth = 1.5f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };
        public override SKPaint PianoRollBarlineHeadPaint { get; set; } = new SKPaint
        {
            Color = SKColor.Parse("#c0ffffff"),
            StrokeWidth = 2f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };
        public override SKPaint PianoRollBeatlineHeadPaint { get; set; } = new SKPaint
        {
            Color = SKColor.Parse("#80ffffff"),
            StrokeWidth = 2f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };
        public override SKColor TempoSignatureText { get; set; } = SKColor.Parse("#FFFFFF");
        public override SKColor TimeSignatureText { get; set; } = SKColor.Parse("#FFFFFF");
        public override SKColor TrackNote { get; set; } = SKColor.Parse("#ffffff");
        public override SKColor ActiveNoteEditModeButton { get; set; } = SKColor.Parse("#FF4081");
        public override SKColor SelectedPartBorder { get; set; } = SKColor.Parse("#FFFFFF");
        public override SKColor TimeLineBackground { get; set; } = SKColor.Parse("#101010");
        public override SKPaint PianoRollPlaybackPosLinePaint { get; set; } = new SKPaint
        {
            Color = SKColor.Parse("#e0ffffff"),
            StrokeWidth = 3,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };
        public override SKColor LyricsText { get; set; } = SKColor.Parse("#e0FFFFFF");
        public override SKColor SelectedNoteBorder { get; set; } = SKColor.Parse("#FFFFFF");
        public override SKColor PhonemePosLine { get; set; } = SKColor.Parse("#FFFFFF");
        public override SKPaint PhonemeTextPaint { get; set; } = new SKPaint
        {
            Color = SKColor.Parse("#FFFFFF"),
            Style = SKPaintStyle.Fill,
        };
        public override SKColor PitchLine { get; set; } = SKColor.Parse("#80FFFFFF");
        public override SKPaint TrackHorizontalLinePaint { get; set; } = new SKPaint
        {
            Color = SKColor.Parse("#c0000000"),
            StrokeWidth = 3,
            Style = SKPaintStyle.Fill
        };
        public override SKColor PianoRollShadow { get; set; } = SKColor.Parse("#d0909090");
        public override SKColor PartLabel { get; set; } = SKColor.Parse("#FFFFFF");
        public override SKColor Primary { get; set; } = SKColor.Parse("#fe71a3");
        public override SKPaint ExpressionOptionTextPaint { get; set; } = new SKPaint
        {
            Color = SKColor.Parse("#FFFFFF"),
            Style = SKPaintStyle.Fill,
        };
        public override SKPaint ExpressionOptionBoxPaint { get; set; } = new SKPaint
        {
            Color = SKColor.Parse("#FFFFFF").WithAlpha(100),
            Style = SKPaintStyle.Fill,
            StrokeWidth = 2,
        };
        public override SKPaint DrawingCursorPaint { get; set; } = new()
        {
            Color = SKColors.Yellow,
            StrokeWidth = 2,
            IsAntialias = false,
            Style = SKPaintStyle.Stroke,
        };
        public override SKPaint DefaultExpressionStrokePaint { get; set; } = new()
        {
            StrokeWidth = 2,
            Color = SKColors.Gray,
            Style = SKPaintStyle.Stroke
        };
        public override SKPaint EditedExpressionStrokePaint { get; set; } = new()
        {
            StrokeWidth = 4,
            Color = SKColor.Parse("#fe71a3"),
            Style = SKPaintStyle.Stroke
        };
        public override SKPaint DefaultExpressionFillPaint { get; set; } = new()
        {
            Style = SKPaintStyle.Fill,
            Color = SKColors.LightGray.WithAlpha(100),
        };
        public override SKPaint EditedExpressionFillPaint { get; set; } = new()
        {
            Style = SKPaintStyle.Fill,
            Color = SKColor.Parse("#fe71a3").WithAlpha(150),
        };
    }
}

