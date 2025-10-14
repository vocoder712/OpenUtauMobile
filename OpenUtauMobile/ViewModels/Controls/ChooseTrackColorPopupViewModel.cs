using DynamicData.Binding;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
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
    public partial class ChooseTrackColorPopupViewModel : ReactiveObject
    {
        [Reactive] public ObservableCollectionExtended<KeyValuePair<string, Color>> TrackColors { get; set; } = []; // 轨道颜色集合

        public ChooseTrackColorPopupViewModel()
        {
            Task.Run(RefreshTrackColors);
        }

        public void RefreshTrackColors()
        {
            TrackColors.Clear();
            foreach (KeyValuePair<string, Color> trackColor in ViewConstants.TrackMauiColors)
            {
                TrackColors.Add(trackColor);
            }
        }

    }
}
