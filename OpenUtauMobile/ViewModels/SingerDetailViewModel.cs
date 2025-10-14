using OpenUtau.Core.Ustx;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenUtauMobile.ViewModels
{
    public partial class SingerDetailViewModel : ReactiveObject
    {
        [Reactive] public USinger Singer { get; set; } = null!;
        public void Init(USinger singer)
        {
            Singer = singer;
        }
    }
}
