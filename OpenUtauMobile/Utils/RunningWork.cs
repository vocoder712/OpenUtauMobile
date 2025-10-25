using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenUtauMobile.Resources.Strings;

namespace OpenUtauMobile.Utils
{
    public enum WorkType
    {
        Phonemize,
        Render,
        Export,
        ReadWave,
        RenderPitch,
        LoadingProject,
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
            { WorkType.RenderPitch, Colors.Teal.WithAlpha(.2f) },
            { WorkType.LoadingProject, Colors.Brown.WithAlpha(.2f) }

        };
        public Dictionary<WorkType, string> WorkNames = new()
        {
            { WorkType.Phonemize, Resources.Strings.AppResources.WorkTitlePhonemize },
            { WorkType.Render, Resources.Strings.AppResources.WorkTitleRender },
            { WorkType.Export, Resources.Strings.AppResources.WorkTitleExport },
            { WorkType.ReadWave, Resources.Strings.AppResources.WorkTitleReadWave },
            { WorkType.Other, Resources.Strings.AppResources.WorkTitleOther },
            { WorkType.RenderPitch, Resources.Strings.AppResources.WorkTitleRenderPitch },
            { WorkType.LoadingProject, Resources.Strings.AppResources.WorkTitleLoadingProject },
        };
        public string Id { get; set; } = new Guid().ToString();
        public string Title => WorkNames.GetValueOrDefault(Type, AppResources.AtWork);
        public WorkType Type { get; set; }
        public double? Progress { get; set; } // 0 - 1
        public string Detail { get; set; } = string.Empty;
        public CancellationTokenSource? CancellationTokenSource { get; set; } = null;
        public Color Color => WorkColors.FirstOrDefault(x => x.Key == Type).Value ?? Colors.Transparent;
    }
}
