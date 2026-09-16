using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;

namespace OpenUtauMobile.ViewModels;

/// <summary>保留原始覆盖，确保音素索引尚未生成时也能完整撤销属性编辑。</summary>
internal sealed class NotePropertiesExpressionCommand : SetNotesSameExpressionCommand
{
    private readonly UExpression[][] originals;

    public NotePropertiesExpressionCommand(UProject project, UTrack track, UVoicePart part,
        IEnumerable<UNote> notes, string abbr, float? value) : base(project, track, part, notes, abbr, value)
    {
        originals = this.notes.Select(note => note.phonemeExpressions.Where(expression => expression.abbr == abbr)
            .Select(expression => new UExpression(expression.abbr)
            {
                index = expression.index,
                value = expression.value,
                descriptor = expression.descriptor
            }).ToArray()).ToArray();
    }

    public override void Unexecute()
    {
        for (int i = 0; i < notes.Length; i++)
        {
            notes[i].phonemeExpressions.RemoveAll(expression => expression.abbr == Key);
            notes[i].phonemeExpressions.AddRange(originals[i]);
        }
    }
}
