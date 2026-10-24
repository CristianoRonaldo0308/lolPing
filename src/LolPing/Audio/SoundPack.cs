using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using LolPing.Core;

namespace LolPing.Audio;

/// <summary>
/// Gói âm thanh của người dùng: một thư mục chứa .ogg / .wav / .mp3.
/// File được đọc tại chỗ (không chép đi đâu) và ghép với tín hiệu theo tên file,
/// ví dụ "Caution_ping_SFX.ogg" → Nguy hiểm, hoặc đặt đúng id như "danger.wav".
/// </summary>
public static class SoundPack
{
    private static readonly string[] Extensions = { ".ogg", ".wav", ".mp3" };
    private const int MaxSeconds = 8;

    private static volatile Dictionary<string, float[]> _sounds = new();

    public static int Count => _sounds.Count;
    public static IReadOnlyCollection<string> LoadedIds => _sounds.Keys;

    /// <summary>Báo khi nạp xong (luôn gọi trên luồng giao diện).</summary>
    public static event Action? Changed;

    public static float[]? TryGet(string id) => _sounds.TryGetValue(id, out var s) ? s : null;

    public static void Load(string? folder)
    {
        var ui = System.Windows.Application.Current?.Dispatcher;
        Task.Run(() =>
        {
            var dir = PackMirror.Prepare(folder, AppPaths.SoundPackCopy, Extensions);
            if (dir == null)
            {
                _sounds = new();
                ui?.InvokeAsync(() => Changed?.Invoke());
                return;
            }
            var result = new Dictionary<string, float[]>();
            foreach (var file in Directory.EnumerateFiles(dir).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                if (!Extensions.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
                var id = PackNames.Match(Path.GetFileNameWithoutExtension(file));
                if (id == null || result.ContainsKey(id)) continue;
                try
                {
                    result[id] = Decode(file);
                }
                catch (Exception ex)
                {
                    Log.Error("Decode sound " + file, ex);
                }
            }
            _sounds = result;
            Log.Write($"Sound pack '{folder}': {result.Count} sounds ({string.Join(", ", result.Keys)})");
            ui?.InvokeAsync(() => Changed?.Invoke());
        });
    }

    /// <summary>Giải mã về mono, 44.1 kHz, float — cùng định dạng với âm tổng hợp để trộn chung.</summary>
    /// <summary>Dùng chung cho kho âm thanh (ClipStore).</summary>
    public static float[] Decode(string path, int maxSeconds = MaxSeconds)
    {
        using WaveStream reader = Path.GetExtension(path).Equals(".ogg", StringComparison.OrdinalIgnoreCase)
            ? new VorbisWaveReader(path)
            : new AudioFileReader(path);

        var source = reader as ISampleProvider ?? reader.ToSampleProvider();
        int channels = source.WaveFormat.Channels;
        int rate = source.WaveFormat.SampleRate;

        var all = new List<float>();
        var buf = new float[rate * channels];
        int limit = rate * channels * maxSeconds, read;
        while (all.Count < limit && (read = source.Read(buf, 0, buf.Length)) > 0)
            all.AddRange(buf.Take(read));

        var mono = new float[all.Count / channels];
        for (int i = 0; i < mono.Length; i++)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++) sum += all[i * channels + c];
            mono[i] = sum / channels;
        }

        if (rate == Synth.Rate) return mono;

        var resampler = new WdlResamplingSampleProvider(new ArrayProvider(mono, rate), Synth.Rate);
        var output = new List<float>();
        var chunk = new float[Synth.Rate];
        while ((read = resampler.Read(chunk, 0, chunk.Length)) > 0)
            output.AddRange(chunk.Take(read));
        return output.ToArray();
    }

    private sealed class ArrayProvider : ISampleProvider
    {
        private readonly float[] _data;
        private int _pos;

        public ArrayProvider(float[] data, int rate)
        {
            _data = data;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, 1);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, _data.Length - _pos);
            if (n <= 0) return 0;
            Array.Copy(_data, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }
    }
}
