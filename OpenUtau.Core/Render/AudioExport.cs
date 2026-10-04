using System;
using System.IO;
using System.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace OpenUtau.Core.Render {
    public record AudioExportTrack(int TrackNo, bool Export, double Gain);
    public record AudioExportProgress(int TrackNo, double Percent);
    public class TrackRenderProgressNotification : UNotification {
        public int TrackNo { get; }
        public double Percent { get; }
        public override bool Silent => true;
        public TrackRenderProgressNotification(int trackNo, double percent) { TrackNo = trackNo; Percent = percent; }
    }
    public enum AspirationOutput { None, Separate, Combined }
    public record AudioExportOptions(int Channels, int BitDepth, int SampleRate, AspirationOutput Aspiration = AspirationOutput.Combined);

    public static class AudioExport {
        public static void SeparateWave(string inputPath, string harmonicPath, string noisePath,
                Func<float[], float[]> separate, CancellationToken cancellation) {
            using var reader = new WaveFileReader(inputPath);
            int channels = reader.WaveFormat.Channels;
            int rate = reader.WaveFormat.SampleRate;
            var format = WaveFormat.CreateIeeeFloatWaveFormat(rate, channels);
            using var harmonicWriter = new WaveFileWriter(harmonicPath, format);
            using var noiseWriter = new WaveFileWriter(noisePath, format);
            int frames = checked((int)(reader.Length / reader.WaveFormat.BlockAlign));
            int blockSize = rate * 5;
            int context = rate / 5;
            for (int start = 0; start < frames; start += blockSize) {
                cancellation.ThrowIfCancellationRequested();
                int length = Math.Min(blockSize, frames - start);
                int from = Math.Max(0, start - context);
                int to = Math.Min(frames, start + length + context);
                reader.Position = (long)from * reader.WaveFormat.BlockAlign;
                var input = reader.ToSampleProvider();
                var samples = new float[(to - from) * channels];
                int read = 0;
                while (read < samples.Length) {
                    int count = input.Read(samples, read, samples.Length - read);
                    if (count == 0) throw new EndOfStreamException("Unexpected end of separation input.");
                    read += count;
                }
                var harmonic = new float[length * channels];
                var noise = new float[length * channels];
                for (int channel = 0; channel < channels; channel++) {
                    var mono = new float[to - from];
                    for (int frame = from; frame < to; frame++) { mono[frame - from] = samples[(frame - from) * channels + channel]; }
                    var separated = separate(mono);
                    if (separated.Length != mono.Length) throw new InvalidDataException("Separation changed the audio length.");
                    for (int frame = 0; frame < length; frame++) {
                        int index = frame * channels + channel;
                        harmonic[index] = separated[start - from + frame];
                        noise[index] = samples[(start - from + frame) * channels + channel] - harmonic[index];
                    }
                }
                harmonicWriter.WriteSamples(harmonic, 0, harmonic.Length);
                noiseWriter.WriteSamples(noise, 0, noise.Length);
            }
        }
        public static void WriteWave(string path, ISampleProvider source, AudioExportOptions options) {
            if (options.Channels != 1 && options.Channels != 2) throw new ArgumentOutOfRangeException(nameof(options.Channels));
            if (options.BitDepth != 16 && options.BitDepth != 24 && options.BitDepth != 32) throw new ArgumentOutOfRangeException(nameof(options.BitDepth));
            if (options.SampleRate < 8000 || options.SampleRate > 192000) throw new ArgumentOutOfRangeException(nameof(options.SampleRate));
            if (source.WaveFormat.Channels == 2 && options.Channels == 1) {
                source = new StereoToMonoSampleProvider(source) { LeftVolume = .5f, RightVolume = .5f };
            } else if (source.WaveFormat.Channels == 1 && options.Channels == 2) {
                source = new MonoToStereoSampleProvider(source);
            }
            if (source.WaveFormat.SampleRate != options.SampleRate) {
                source = new WdlResamplingSampleProvider(source, options.SampleRate);
            }
            var format = options.BitDepth == 32
                ? WaveFormat.CreateIeeeFloatWaveFormat(options.SampleRate, options.Channels)
                : new WaveFormat(options.SampleRate, options.BitDepth, options.Channels);
            using var writer = new WaveFileWriter(path, format);
            var samples = new float[8192];
            var bytes = new byte[samples.Length * (options.BitDepth / 8)];
            int count;
            while ((count = source.Read(samples, 0, samples.Length)) > 0) {
                if (options.BitDepth == 32) {
                    writer.WriteSamples(samples, 0, count);
                } else {
                    int width = options.BitDepth / 8;
                    int max = (1 << (options.BitDepth - 1)) - 1;
                    int min = -(1 << (options.BitDepth - 1));
                    for (int i = 0; i < count; i++) {
                        int value = (int)Math.Clamp(Math.Round(samples[i] * max), min, max);
                        for (int b = 0; b < width; b++) { bytes[i * width + b] = (byte)(value >> (8 * b)); }
                    }
                    writer.Write(bytes, 0, count * width);
                }
            }
        }
        public static string SafeFileName(string name) {
            foreach (var character in Path.GetInvalidFileNameChars()) { name = name.Replace(character, '_'); }
            name = name.Trim().TrimEnd('.');
            return string.IsNullOrWhiteSpace(name) ? "untitled" : name;
        }
    }
}
