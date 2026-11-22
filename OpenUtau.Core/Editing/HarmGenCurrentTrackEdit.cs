using System.Collections.Generic;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Editing {
    /// <summary>
    /// Mobile entry point for the harmony generator: harmonizes the track of the part being edited
    /// with the chosen style (Pop, Choir, Cinematic or Randomized Human), automatic key detection and
    /// default voice ranges. Only the style and the number of voices are configurable.
    /// </summary>
    public class HarmGenCurrentTrackEdit : BatchEdit {
        private readonly int numVoices;
        private readonly string styleName;

        public HarmGenCurrentTrackEdit(int numVoices, string styleName = "Pop") {
            this.numVoices = System.Math.Clamp(numVoices, 1, 8);
            this.styleName = styleName;
        }

        public string Name => "BatchEdit.Action.HarmonyGenerator";

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            var config = new HarmGenConfig {
                TrackIndexes = new List<int> { part.trackNo },
                NumVoices = numVoices,
                StyleName = styleName,
            };
            new HarmGenEdit(config).Run(project, part, selectedNotes, docManager);
        }
    }
}
