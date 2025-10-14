using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenUtauMobile.Utils
{
    public enum WorkType
    {
        Phonemize,
        Render,
        Export,
        ReadWave,
        RenderPitch,
        Other
    }
    public class RunningWork
    {
        public Dictionary<WorkType, Color> WorkColors = new()
        {
            { WorkType.Phonemize, Colors.Blue.WithAlpha(0.2f) },
            { WorkType.Render, Colors.Green.WithAlpha(0.2f) },
            { WorkType.Export, Colors.Orange.WithAlpha(.2f) },
            { WorkType.ReadWave, Colors.Purple.WithAlpha(.2f) },
            { WorkType.Other, Colors.Gray.WithAlpha(.2f) },
            { WorkType.RenderPitch, Colors.Teal.WithAlpha(.2f) }

        };
        public Dictionary<WorkType, string> WorkNames = new()
        {
            { WorkType.Phonemize, "正在音素化" },
            { WorkType.Render, "正在渲染" },
            { WorkType.Export, "正在导出" },
            { WorkType.ReadWave, "正在渲染音频波形" },
            { WorkType.Other, "工作中" },
            { WorkType.RenderPitch, "正在渲染音高" }
        };
        public string Id { get; set; } = new Guid().ToString();
        public string Title => WorkNames.GetValueOrDefault(Type, "工作中");
        public WorkType Type { get; set; }
        public double? Progress { get; set; } // 0 - 1
        public string Detail { get; set; } = string.Empty;
        public CancellationTokenSource? CancellationTokenSource { get; set; } = null;
        public Color Color => WorkColors.FirstOrDefault(x => x.Key == Type).Value ?? Colors.Transparent;
    }
}
