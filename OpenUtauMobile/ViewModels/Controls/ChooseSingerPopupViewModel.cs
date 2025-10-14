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
    public partial class ChooseSingerPopupViewModel : ReactiveObject
    {
        [Reactive] public ObservableCollectionExtended<USinger> Singers { get; set; } = []; // 歌手集合

        public ChooseSingerPopupViewModel()
        {
            Task.Run(() => RefreshSingers());
        }

        public void RefreshSingers()
        {
            Singers.Clear();
            foreach (var singer in SingerManager.Inst.SingerGroups.Values.SelectMany(anySinger => anySinger))
            {
                Singers.Add(singer);
            }
        }

    }
}
