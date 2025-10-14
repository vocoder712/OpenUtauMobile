using DynamicData.Binding;
using OpenUtau.Core;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace OpenUtauMobile.ViewModels.Controls
{
    public partial class ExportAudioPopupViewModel : ReactiveObject
    {
        //public ObservableCollectionExtended<KeyValuePair<int, string>> MixType { get; } =
        //    [
        //        new KeyValuePair<int, string>(0, "Mixdown to a single file"),
        //        new KeyValuePair<int, string>(1, "Render each track to separate files"),
        //    ];
        //[Reactive] public KeyValuePair<int, string> SelectedMixType { get; set; }
        [Reactive] public string SelectedMixType { get; set; } = "mixdown";
        [Reactive] public int SampleRate { get; set; } = 44100;
        [Reactive] public int BitDepth { get; set; } = 16;

        public ExportAudioPopupViewModel()
        {
            //SelectedMixType = MixType.First();
        }
        public bool Export(string file)
        {
            switch (SelectedMixType)
            {
                case "mixdown":
                    _ = PlaybackManager.Inst.RenderMixdown(DocManager.Inst.Project, file);
                    return true;
                case "tracks":
                    _ = PlaybackManager.Inst.RenderToFiles(DocManager.Inst.Project, file);
                    return true;
                default:
                    return false;
            }
        }
    }
}
