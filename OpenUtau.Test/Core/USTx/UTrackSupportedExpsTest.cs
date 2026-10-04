using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Core {
    /// <summary>
    /// Which parameters a track offers is the renderer's answer, and the renderer can support
    /// expressions the project itself does not register (DiffSinger's velocity and energy curves
    /// only reach the project through the expressions dialog). Those have to be listed too.
    /// </summary>
    public class UTrackSupportedExpsTest {
        class StubRenderer : IRenderer {
            readonly HashSet<string> supported;
            public StubRenderer(params string[] abbrs) => supported = new HashSet<string>(abbrs);
            public USingerType SingerType => USingerType.Classic;
            public bool SupportsRenderPitch => false;
            public bool SupportsExpression(UExpressionDescriptor descriptor) => supported.Contains(descriptor.abbr);
            public RenderResult Layout(RenderPhrase phrase) => new RenderResult();
            public Task<RenderResult> Render(RenderPhrase phrase, Progress progress, int trackNo,
                    CancellationTokenSource cancellation, bool isPreRender = false, RenderPhraseEvents? renderEvents = null) {
                throw new NotImplementedException();
            }
            public RenderPitchResult LoadRenderedPitch(RenderPhrase phrase) => null;
            public UExpressionDescriptor[] GetSuggestedExpressions(USinger singer, URenderSettings renderSettings) =>
                Array.Empty<UExpressionDescriptor>();
        }

        static UTrack CreateTrack(UProject project, IRenderer renderer, params UExpressionDescriptor[] trackExpressions) {
            var track = project.tracks[0];
            track.RendererSettings = new URenderSettings { Renderer = renderer };
            track.TrackExpressions = trackExpressions.ToList();
            return track;
        }

        [Fact]
        public void ListsTheProjectExpressionsTheRendererSupports() {
            var project = new UProject();
            project.RegisterExpression(new UExpressionDescriptor("volume", "vol", 0, 200, 100));
            project.RegisterExpression(new UExpressionDescriptor("tension (curve)", "tenc", -100, 100, 0) {
                type = UExpressionType.Curve,
            });
            var track = CreateTrack(project, new StubRenderer("vol"));

            Assert.Equal(new[] { "vol" }, track.GetSupportedExps(project).Select(exp => exp.abbr));
        }

        [Fact]
        public void ListsExpressionsOnlyTheTrackHas() {
            var project = new UProject();
            project.RegisterExpression(new UExpressionDescriptor("volume", "vol", 0, 200, 100));
            var track = CreateTrack(project, new StubRenderer("velc"),
                new UExpressionDescriptor("velocity (curve)", "velc", 0, 200, 100) { type = UExpressionType.Curve });

            Assert.Equal(new[] { "velc" }, track.GetSupportedExps(project).Select(exp => exp.abbr));
        }

        [Fact]
        public void ListsNothingWithoutARenderer() {
            var project = new UProject();
            project.RegisterExpression(new UExpressionDescriptor("volume", "vol", 0, 200, 100));
            var track = CreateTrack(project, new StubRenderer("vol"));

            track.RendererSettings.Renderer = null;

            Assert.Empty(track.GetSupportedExps(project));
        }
    }
}
