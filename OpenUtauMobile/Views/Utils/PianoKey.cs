using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenUtauMobile.Views.Utils
{
    public class PianoKey(int noteNum)
    {
        public int NoteNum { get; set; } = noteNum;
        public string NoteName { get; set; } = OpenUtau.Core.MusicMath.GetToneName(noteNum);
        public bool IsBlackKey { get; set; } = OpenUtau.Core.MusicMath.IsBlackKey(noteNum);
    }
}
