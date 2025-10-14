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
    public partial class PianoRollSnapDivPopupViewModel : ReactiveObject
    {
        [Reactive] public ObservableCollectionExtended<int> SnapDivs { get; set; } = []; // 常见量化
        [Reactive] public int CurrentSnapDiv { get; set; } // 当前量化

        public PianoRollSnapDivPopupViewModel()
        {

        }

        public void Initialize(int currentSnapDiv, int[] snapDivs)
        {
            SnapDivs.Clear();
            foreach (var snapDiv in snapDivs)
            {
                SnapDivs.Add(snapDiv);
            }
            CurrentSnapDiv = currentSnapDiv;
        }

    }
}
