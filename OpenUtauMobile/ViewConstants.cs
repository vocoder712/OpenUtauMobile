using System.Collections.Generic;
using Avalonia.Input;
using OpenUtauMobile.Helpers;

namespace OpenUtauMobile
{
    static class ViewConstants
    {
        /// <summary>
        /// 量化吸附分度选项列表。
        /// -1 = 自动（根据 TickWidth 动态推导）；0 = 关闭（自由移动）；正数 = 固定分度（4=全音符…48=附点三十二分音符）。
        /// </summary>
        public static readonly IReadOnlyList<int> SnapDivOptions = [-1, 0, 4, 8, 12, 16, 24, 32, 48];

        /// <summary>
        /// 将吸附分度整数转换为界面显示标签。
        /// </summary>
        public static string SnapDivToLabel(int div) => div switch
        {
            -1 => L.S("Editor.Quantize.Auto"),
            0 => L.S("Editor.Quantize.Off"),
            _ => $"1/{div}",
        };

        // 走带编曲区横向缩放（EditorViewModel.TickWidth，单位：像素/Tick）
        public const double TickWidthMax = 240.0 / 480.0;
        public const double TickWidthMin = 8.0 / 480.0;
        public const double TickWidthDefault = 24.0 / 480.0;


        /// <summary>
        /// 钢琴卷帘自动量化时的最小分度线宽度（像素）
        /// </summary>
        public const double PianoRollMinTicklineWidth = 48d;

        /// <summary>
        /// 走带编曲自动量化时的最小分度线宽度（像素）
        /// </summary>
        public const double TrackMinTicklineWidth = 12d;

        // 标尺区高度（小节号、BPM、拍号显示区域）
        public const double TickRulerHeight = 24;

        // 钢琴卷帘标尺区高度（不与走带编曲区共享，便于单独布局）
        public const double PianoRollTickRulerHeight = 24;

        // 默认吸附分度（1/16 音符）
        public const int DefaultSnapDiv = 16;

        // 走带编曲区轨道头列宽（像素）
        public const double TrackHeaderWidthExpanded = 300;
        public const double TrackHeaderWidthCollapsed = 64;

        // 走带编曲区轨道高度（EditorViewModel.TrackHeight，单位：像素）
        public const double TrackHeightMax = 144;
        public const double TrackHeightMin = 44;
        public const double TrackHeightDefault = 104;
        public const double TrackHeightDelta = 20;

        // Part 调整手柄
        public const double ResizeHandleVisualWidth = 14.0; // 视觉宽度（px）
        public const double ResizeHandleHitWidth = 24.0; // 命中测试宽度（px，大于视觉，便于移动端触碰）

        // Note 调整手柄（在音符矩形右侧外部绘制，带 gap）
        public const double NoteResizeHandleVisualWidth = 24.0; // 视觉宽度（px）
        public const double NoteResizeHandleGap = 6.0; // 手柄左边缘与音符右边缘的间距（px）
        public const double NoteResizeHandleHitWidth = 32.0; // 命中测试宽度（px，移动端友好）

        // 钢琴卷帘区横向缩放（PianoRollViewModel.TickWidth，单位：像素/Tick）
        public const double PianoRollTickWidthMax = 1;
        public const double PianoRollTickWidthMin = 8.0 / 480.0;
        public const double PianoRollTickWidthDefault = 48.0 / 480.0;

        public const double NoteHeightMax = 128;
        public const double NoteHeightMin = 16;

        // 钢琴卷帘左侧琴键栏固定宽度（像素）
        public const double PianoKeysWidth = 56;

        // 走带区/钢琴卷帘分割拖拽
        public const double SplitDragThreshold = 6.0;
        public const double PianoRollHeaderHeight = 48;

        // EditorView 响应式分割器
        public const double EditorSplitLineThickness = 1.0;
        public const double EditorSplitHandleWidth = 48.0;
        public const double EditorSplitHandleHeight = 24.0;
        public const double EditorSplitHandleEdgeInset = 8.0;

        // 默认分割位置
        public const double EditorSplitDefaultTrackExtentPortrait = 200.0;
        public const double EditorSplitDefaultTrackRatioLandscape = 0.42;

        public const int MaxTone = 12 * 11;

        /// <summary>
        /// 编辑模式列表一个操作按钮的边长
        /// </summary>
        public const double EditModeItemHeight = 24;

        public const double EditModeItemSpacing = 4;

        public static readonly Cursor cursorCross = new Cursor(StandardCursorType.Cross);
        public static readonly Cursor cursorHand = new Cursor(StandardCursorType.Hand);
        public static readonly Cursor cursorNo = new Cursor(StandardCursorType.No);
        public static readonly Cursor cursorSizeAll = new Cursor(StandardCursorType.SizeAll);
        public static readonly Cursor cursorSizeNS = new Cursor(StandardCursorType.SizeNorthSouth);
        public static readonly Cursor cursorSizeWE = new Cursor(StandardCursorType.SizeWestEast);

        public const int PosMarkerHightlighZIndex = -100;

        public const int ResizeMargin = 8;

        public const int MinTrackCount = 8;
        public const int MinQuarterCount = 256;
        public const int SpareTrackCount = 4;
        public const int SpareQuarterCount = 16;

        public const double TickMinDisplayWidth = 6;
        public const double NoteMinDisplayWidth = 2;

        public const int PartRectangleZIndex = 100;
        public const int PartThumbnailZIndex = 200;
        public const int PartElementZIndex = 200;

        public const int ExpressionHiddenZIndex = 0;
        public const int ExpressionVisibleZIndex = 200;
        public const int ExpressionShadowZIndex = 100;

        // ── Settings page ─────────────────────────────────────────────
        /// <summary>
        /// 设置页面侧边栏自动展开/收起的宽度阈值（px）。
        /// 控件宽度 &gt;= 此值时展开，否则收起为仅图标模式。
        /// </summary>
        public const double SettingsSidebarBreakpoint = 600;

        /// <summary>
        /// 设置页面侧边栏展开宽度（px）。
        /// </summary>
        public const double SettingsNavExpandedWidth = 240;

        /// <summary>
        /// 设置页面侧边栏收起宽度（px，仅图标）。
        /// </summary>
        public const double SettingsNavCollapsedWidth = 64;

        /// <summary>
        /// 是否启用基准测试模式
        /// </summary>
        public static bool EnableBenchMarkTest { get; set; } = true;
    }
}