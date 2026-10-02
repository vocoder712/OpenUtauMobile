using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;

namespace OpenUtauMobile.Services
{
    public static class ExpressionCatalog
    {
        public static IReadOnlyList<UExpressionDescriptor> GetTrackExpressions(UProject project, UTrack track)
        {
            List<UExpressionDescriptor> descriptors = [];
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            IEnumerable<string> abbreviations = project.expressions.Keys.Concat(track.TrackExpressions.Select(expression => expression.abbr));
            foreach (string abbreviation in abbreviations)
            {
                if (!seen.Add(abbreviation) || !track.TryGetExpDescriptor(project, abbreviation, out UExpressionDescriptor descriptor))
                {
                    continue;
                }
                descriptors.Add(descriptor);
            }
            return descriptors;
        }

        public static IReadOnlyList<UExpressionDescriptor> GetSupportedTrackExpressions(UProject project, UTrack track)
        {
            IRenderer? renderer = track.RendererSettings.Renderer;
            if (renderer == null) return Array.Empty<UExpressionDescriptor>();
            return GetTrackExpressions(project, track).Where(renderer.SupportsExpression).ToArray();
        }

        public static bool IsIncluded(UTrack track, UExpressionDescriptor descriptor)
        {
            return track.RendererSettings.Renderer?.SupportsExpression(descriptor) == true;
        }
    }
}
