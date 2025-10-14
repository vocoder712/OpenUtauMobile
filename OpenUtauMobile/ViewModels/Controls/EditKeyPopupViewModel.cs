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
    public partial class EditKeyPopupViewModel : ReactiveObject
    {
        [Reactive] public List<int> Keys { get; set; } = [0,1,2,3,4,5,6,7,8,9,10,11]; // 常见调性
        [Reactive] public int CurrentKey { get; set; } // 当前调性

        public EditKeyPopupViewModel()
        {

        }

        public void Initialize(int currentKey)
        {
            CurrentKey = currentKey;
        }

    }
}
