using System;
using System.IO;
using System.Linq;
using System.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using OpenUtau.Core.Render;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.UiTest {
    public class AudioExportTest {
        sealed class Samples : ISampleProvider {
            readonly float[] samples;
            int position;
            public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
            public Samples(int frames = 4410) {
                samples = Enumerable.Range(0, frames * 2).Select(i => i % 2 == 0 ? .25f : .75f).ToArray();
            }
            public int Read(float[] buffer, int offset, int count) {
                count = Math.Min(count, samples.Length - position);
                Array.Copy(samples, position, buffer, offset, count);
                position += count;
                return count;
            }
        }
        [Theory]
        [InlineData(1, 16, 44100)]
        [InlineData(2, 24, 48000)]
        [InlineData(2, 32, 96000)]
        public void WritesRequestedFormatAndPreservesLevelAndDuration(int channels, int bits, int rate) {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
            try {
                AudioExport.WriteWave(path, new Samples(), new AudioExportOptions(channels, bits, rate));
                using var wave = new WaveFileReader(path);
                Assert.Equal(channels, wave.WaveFormat.Channels);
                Assert.Equal(bits, wave.WaveFormat.BitsPerSample);
                Assert.Equal(rate, wave.WaveFormat.SampleRate);
                Assert.Equal(bits == 32 ? WaveFormatEncoding.IeeeFloat : WaveFormatEncoding.Pcm, wave.WaveFormat.Encoding);
                Assert.InRange(wave.TotalTime.TotalSeconds, .098, .102);
                var decoded = wave.ToSampleProvider();
                var samples = new float[rate * channels];
                int count = decoded.Read(samples, 0, samples.Length);
                int frame = count / channels / 2;
                Assert.InRange(samples[frame * channels], channels == 1 ? .499f : .249f, channels == 1 ? .501f : .251f);
                if (channels == 2) Assert.InRange(samples[frame * 2 + 1], .749f, .751f);
            } finally { File.Delete(path); }
        }
        [Fact]
        public void PeriodicAndNonPeriodicFilesRecombineAcrossChunkBoundaries() {
            var directory = Path.Combine(Path.GetTempPath(), "openutau-export-test-" + Guid.NewGuid());
            Directory.CreateDirectory(directory);
            try {
                var original = Path.Combine(directory, "original.wav");
                var harmonic = Path.Combine(directory, "harmonic.wav");
                var noise = Path.Combine(directory, "noise.wav");
                AudioExport.WriteWave(original, new Samples(44100 * 6), new AudioExportOptions(2, 32, 44100));
                AudioExport.SeparateWave(original, harmonic, noise, x => x.Select(v => v * .3f).ToArray(), CancellationToken.None);
                using var source = new WaveFileReader(original);
                using var periodic = new WaveFileReader(harmonic);
                using var nonPeriodic = new WaveFileReader(noise);
                Assert.Equal(source.Length, periodic.Length);
                Assert.Equal(source.Length, nonPeriodic.Length);
                var a = source.ToSampleProvider();
                var b = periodic.ToSampleProvider();
                var c = nonPeriodic.ToSampleProvider();
                var ab = new float[4096]; var bb = new float[4096]; var cb = new float[4096];
                int count;
                while ((count = a.Read(ab, 0, ab.Length)) > 0) {
                    Assert.Equal(count, b.Read(bb, 0, bb.Length));
                    Assert.Equal(count, c.Read(cb, 0, cb.Length));
                    for (int i = 0; i < count; i++) Assert.InRange(Math.Abs(ab[i] - bb[i] - cb[i]), 0, 1e-6);
                }
            } finally { Directory.Delete(directory, true); }
        }

        [Fact]
        public void InstalledHnsepModelSeparatesPeriodicAndNonPeriodicSignal() {
            HeadlessUi.Run(() => {
                MainWindowTest.InitCore();
                Assert.SkipWhen(PackageManager.Inst.GetInstalledPath(OpenUtau.Core.Analysis.Hnsep.PackageId) == null, "HNSEP model package is required.");
                var rng = new Random(42);
                var input = Enumerable.Range(0, 22050).Select(i => (float)(.2 * Math.Sin(2 * Math.PI * 220 * i / 44100)
                    + .02 * (rng.NextDouble() * 2 - 1))).ToArray();
                var harmonic = OpenUtau.Core.Analysis.Hnsep.Instance.Harmonic(input);
                Assert.Equal(input.Length, harmonic.Length);
                Assert.All(harmonic, value => Assert.True(float.IsFinite(value)));
                Assert.True(harmonic.Sum(x => x * x) > .001);
                Assert.True(input.Zip(harmonic, (a, b) => (a - b) * (a - b)).Sum() > .001);
            });
        }

        [Theory]
        [InlineData(AspirationOutput.Combined, 2)]
        [InlineData(AspirationOutput.None, 2)]
        [InlineData(AspirationOutput.Separate, 4)]
        public void ExportPipelineWritesMixAndSelectedStemWithGainAndSkipsMutedTrack(AspirationOutput aspiration, int expectedFiles) {
            HeadlessUi.Run(() => {
                MainWindowTest.InitCore();
                Assert.SkipWhen(aspiration != AspirationOutput.Combined && PackageManager.Inst.GetInstalledPath(OpenUtau.Core.Analysis.Hnsep.PackageId) == null,
                    "HNSEP model package is required.");
                var directory = Path.Combine(Path.GetTempPath(), "openutau-export-pipeline-" + Guid.NewGuid());
                Directory.CreateDirectory(directory);
                try {
                    var input = Path.Combine(directory, "input.wav");
                    AudioExport.WriteWave(input, new Samples(), new AudioExportOptions(2, 32, 44100));
                    var project = OpenUtau.Core.Format.Ustx.Create();
                    project.tracks.Clear();
                    project.tracks.Add(new UTrack { TrackNo = 0, TrackName = "Audio" });
                    project.tracks.Add(new UTrack { TrackNo = 1, TrackName = "Muted", Mute = true, Muted = true });
                    var part = new UWavePart { FilePath = input, trackNo = 0, position = 0 };
                    part.Load(project);
                    part.Peaks.GetAwaiter().GetResult();
                    project.parts.Add(part);
                    if (aspiration != AspirationOutput.Combined) {
                        project.parts.Add(new UVoicePart { trackNo = 0, position = 0 });
                    }
                    var export = PlaybackManager.Inst.ExportAudio(project, directory, "bounce", true, .5,
                        new[] { new AudioExportTrack(0, true, .5), new AudioExportTrack(1, true, 1) },
                        new AudioExportOptions(1, 24, 48000, aspiration));
                    var deadline = DateTime.UtcNow.AddSeconds(30);
                    while (!export.IsCompleted && DateTime.UtcNow < deadline) {
                        HeadlessUi.Flush();
                        Thread.Sleep(5);
                    }
                    Assert.True(export.IsCompleted, "Audio export did not finish.");
                    var outputs = export.GetAwaiter().GetResult();
                    Assert.Equal(expectedFiles, outputs.Length);
                    Assert.DoesNotContain(outputs, path => path.Contains("Muted"));
                    using var mix = new WaveFileReader(Path.Combine(directory, "bounce.wav"));
                    using var stem = new WaveFileReader(outputs.Single(path => path.EndsWith("-01-Audio.wav")));
                    var mixSamples = new float[4096]; var stemSamples = new float[4096];
                    int count = mix.ToSampleProvider().Read(mixSamples, 0, mixSamples.Length);
                    Assert.Equal(count, stem.ToSampleProvider().Read(stemSamples, 0, stemSamples.Length));
                    Assert.InRange(count, 1, mixSamples.Length);
                    if (aspiration == AspirationOutput.Combined) Assert.True(stemSamples.Take(count).Max(Math.Abs) > .05);
                    for (int i = 0; i < count; i++) Assert.InRange(Math.Abs(mixSamples[i] - stemSamples[i] * .5), 0, 1e-5);
                } finally { Directory.Delete(directory, true); }
            });
        }
    }
}
