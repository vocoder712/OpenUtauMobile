using OpenUtau.Core.Ustx;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenUtauMobile.Utils.Messages
{
    public class SwitchToNextNoteMessage
    {
        public Action<UNote?> SetNextNoteAction { get; }

        public SwitchToNextNoteMessage(Action<UNote?> setNextNote)
        {
            SetNextNoteAction = setNextNote;
        }
    }
}
