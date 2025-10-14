using CommunityToolkit.Mvvm.ComponentModel;
using DynamicData.Binding;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenUtauMobile.ViewModels
{
    public partial class SingerManageViewModel : ReactiveObject
    {
        [Reactive] public ObservableCollectionExtended<USinger> Singers { get; set; } = []; // 歌手集合

        public SingerManageViewModel()
        {
            RefreshSingers();
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
