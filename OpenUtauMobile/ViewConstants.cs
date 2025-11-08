using OpenUtauMobile.Utils;
using OpenUtauMobile.Views.Utils;
using OpenUtauMobile.Resources.Strings;
using SkiaSharp;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenUtauMobile
{
    public static class ViewConstants
    {
        public const string ReleaseVersionLyrics = "歌声冲破夜幕响彻在新的天地\n机械的心律带动血肉的共鸣"; // v1.1.x 发布版本歌词
        public const int MinTicklineWidth = 24; // 最小刻度线宽度
        public const int TotalPianoKeys = 120; // 总钢琴键数
        public static List<PianoKey> PianoKeys = [];
        public const int TimeLineHeight = 20; // 时间线高度，记得乘以Density
        public const int PianoRollPlaybackLinePos = 200; // 播放线相对钢琴卷帘画布左侧的位置，记得乘以Density
        public const int DivHeight = 50; // 走带与主编辑区分隔的高度，Canvas里面记得乘以Density
        public static List<LanguageOption> LanguageOptions = [
            new LanguageOption("English(US)", "en"),
            new LanguageOption("简体中文", "zh"),
            new LanguageOption("日本語", "ja"),
            new LanguageOption(AppResources.System, ""),
        ];
        //public static Dictionary<int, int> MajorKeyNo = new()
        //{
        //    { 0, 60 }, // C4
        //    { 1, 61 }, // C#4
        //    { 2, 62 }, // D4
        //    { 3, 63 }, // D#4
        //    { 4, 64 }, // E4
        //    { 5, 65 }, // F4
        //    { 6, 66 }, // F#4
        //    { 7, 67 }, // G4
        //    { 8, 68 }, // G#4
        //    { 9, 69 }, // A4
        //    { 10, 70 }, // A#4
        //    { 11, 71 }, // B4
        //};
        #region 轨道颜色常量
        /// <summary>
        /// 轨道颜色，Maui版本
        /// </summary>
        public static readonly Dictionary<string, Color> TrackMauiColors = new()
        {
            { "Pink", Color.FromRgba("#F06292")},
            { "Red", Color.FromRgba("#EF5350")},
            { "Orange", Color.FromRgba("#FF8A65")},
            { "Yellow", Color.FromRgba("#FBC02D")},
            { "Light Green", Color.FromRgba("#CDDC39")},
            { "Green", Color.FromRgba("#66BB6A")},
            { "Light Blue", Color.FromRgba("#4FC3F7")},
            { "Blue", Color.FromRgba("#4EA6EA")},
            { "Purple", Color.FromRgba("#BA68C8")},
            { "Pink2", Color.FromRgba("#E91E63")},
            { "Red2", Color.FromRgba("#D32F2F")},
            { "Orange2", Color.FromRgba("#FF5722")},
            { "Yellow2", Color.FromRgba("#FF8F00")},
            { "Light Green2", Color.FromRgba("#AFB42B")},
            { "Green2", Color.FromRgba("#2E7D32")},
            { "Light Blue2", Color.FromRgba("#1976D2")},
            { "Blue2", Color.FromRgba("#3949AB")},
            { "Purple2", Color.FromRgba("#7B1FA2")},
        };
        /// <summary>
        /// 轨道颜色，SkiaSharp版本
        /// </summary>
        public static readonly Dictionary<string, SKColor> TrackSkiaColors = TrackMauiColors.ToDictionary(kv => kv.Key, kv => SKColor.Parse(kv.Value.ToHex()));
        #endregion
    }
}
