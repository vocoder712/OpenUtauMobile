using System.Collections.Generic;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Editing {
    /// <summary>
    /// Mobile entry point for the harmony generator: harmonizes the track of the part being edited
    /// with the Pop style, automatic key detection and default voice ranges. Only the number of
    /// voices is configurable.
    /// </summary>
    public class HarmGenCurrentTrackEdit : BatchEdit {
        private readonly int numVoices;

        public HarmGenCurrentTrackEdit(int numVoices) {
            this.numVoices = System.Math.Clamp(numVoices, 1, 8);
        }

        public string Name => "BatchEdit.Action.HarmonyGenerator";

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            var config = new HarmGenConfig {
                TrackIndexes = new List<int> { part.trackNo },
                NumVoices = numVoices,
                StyleName = "Pop",
            };
            new HarmGenEdit(config).Run(project, part, selectedNotes, docManager);
        }
    }
}
