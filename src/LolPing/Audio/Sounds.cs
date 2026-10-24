using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using LolPing.Core;

namespace LolPing.Audio;

public enum SynthWave { Sine, Triangle, Soft, Bell }

/// <param name="Start">ms tính từ đầu âm</param>
/// <param name="Dur">ms</param>
/// <param name="F0">tần số đầu (Hz)</param>
/// <param name="F1">tần số cuối (Hz), trượt theo hàm mũ</param>
/// <param name="Decay">tốc độ tắt dần (1/giây)</param>
public readonly record struct Note(double Start, double Dur, double F0, double F1, SynthWave W = SynthWave.Sine, double Amp = 0.5, double Decay = 6);

/// <summary>Tổng hợp âm thanh ngắn bằng code — không cần file âm thanh đi kèm.</summary>
public static class Synth
{
    public const int Rate = 44100;

    public static float[] Render(params Note[] notes)
    {
        double total = 0;
        foreach (var n in notes) total = Math.Max(total, n.Start + n.Dur);
        var buf = new float[(int)((total + 30) / 1000 * Rate)];

        foreach (var n in notes)
        {
            int start = (int)(n.Start / 1000 * Rate);
            int len = (int)(n.Dur / 1000 * Rate);
            double phase = 0;
            int attack = Math.Max(1, (int)(0.004 * Rate));
            int release = Math.Max(1, Math.Min(len / 3, (int)(0.015 * Rate)));

            for (int i = 0; i < len && start + i < buf.Length; i++)
            {
                double t = (double)i / Rate;
                double k = (double)i / len;
                double f = n.F0 * Math.Pow(n.F1 / n.F0, k);
                phase += 2 * Math.PI * f / Rate;

                double s = n.W switch
                {
                    SynthWave.Triangle => 2 / Math.PI * Math.Asin(Math.Sin(phase)),
                    SynthWave.Soft => Math.Tanh(2.5 * Math.Sin(phase)) * 0.8,
                    SynthWave.Bell => Math.Sin(phase) + 0.35 * Math.Sin(2 * phase) + 0.12 * Math.Sin(3.01 * phase),
                    _ => Math.Sin(phase)
                };

                double env = Math.Exp(-n.Decay * t);
                if (i < attack) env *= (double)i / attack;
                if (i > len - release) env *= (double)(len - i) / release;
                buf[start + i] += (float)(s * env * n.Amp);
            }
        }

        float peak = 0;
        foreach (var v in buf) peak = Math.Max(peak, Math.Abs(v));
        if (peak > 0.9f)
            for (int i = 0; i < buf.Length; i++) buf[i] *= 0.9f / peak;
        return buf;
    }
}

public static class Sounds
{
    private static readonly ConcurrentDictionary<string, float[]> Cache = new();

    private static readonly Dictionary<string, Func<float[]>> Recipes = new()
    {
        ["danger"] = () => Synth.Render(
            new Note(0, 110, 988, 880, SynthWave.Triangle, 0.55, 4),
            new Note(130, 170, 740, 587, SynthWave.Triangle, 0.55, 5)),
        ["omw"] = () => Synth.Render(
            new Note(0, 170, 440, 880, SynthWave.Sine, 0.5, 3),
            new Note(150, 160, 1175, 1175, SynthWave.Bell, 0.3, 14)),
        ["push"] = () => Synth.Render(
            new Note(0, 90, 523, 523, SynthWave.Soft, 0.4, 8),
            new Note(80, 90, 659, 659, SynthWave.Soft, 0.4, 8),
            new Note(160, 160, 784, 784, SynthWave.Soft, 0.45, 7)),
        ["assist"] = () => Synth.Render(
            new Note(0, 140, 784, 784, SynthWave.Bell, 0.45, 10),
            new Note(110, 260, 1047, 1047, SynthWave.Bell, 0.45, 8)),
        ["allin"] = () => Synth.Render(
            new Note(0, 140, 196, 247, SynthWave.Soft, 0.6, 5),
            new Note(120, 220, 294, 392, SynthWave.Soft, 0.6, 5),
            new Note(120, 220, 587, 784, SynthWave.Triangle, 0.25, 6)),
        ["needvision"] = () => Synth.Render(
            new Note(0, 320, 1319, 1319, SynthWave.Bell, 0.4, 9),
            new Note(90, 320, 1760, 1760, SynthWave.Bell, 0.25, 11)),
        ["enemyvision"] = () => Synth.Render(
            new Note(0, 260, 880, 698, SynthWave.Bell, 0.45, 8),
            new Note(120, 260, 659, 523, SynthWave.Bell, 0.35, 9)),
        ["missing"] = () => Synth.Render(
            new Note(0, 130, 659, 523, SynthWave.Triangle, 0.5, 5),
            new Note(150, 190, 523, 784, SynthWave.Triangle, 0.5, 5)),
        ["generic"] = () => Synth.Render(
            new Note(0, 260, 1047, 1047, SynthWave.Bell, 0.5, 10)),
        ["bait"] = () => Synth.Render(
            new Note(0, 200, 300, 900, SynthWave.Sine, 0.55, 4),
            new Note(200, 90, 900, 600, SynthWave.Sine, 0.3, 10)),
        ["cleared"] = () => Synth.Render(
            new Note(0, 200, 1568, 1568, SynthWave.Bell, 0.28, 12),
            new Note(60, 200, 1865, 1865, SynthWave.Bell, 0.28, 12),
            new Note(120, 260, 2349, 2349, SynthWave.Bell, 0.28, 10)),

        ["pop"] = () => Synth.Render(new Note(0, 70, 600, 1300, SynthWave.Sine, 0.55, 18)),
        ["laugh"] = () => Synth.Render(
            new Note(0, 70, 700, 900, SynthWave.Soft, 0.4, 14),
            new Note(90, 70, 760, 980, SynthWave.Soft, 0.4, 14),
            new Note(180, 90, 820, 1060, SynthWave.Soft, 0.4, 12)),
        ["love"] = () => Synth.Render(
            new Note(0, 160, 784, 784, SynthWave.Bell, 0.4, 9),
            new Note(120, 260, 988, 988, SynthWave.Bell, 0.4, 8)),
        ["gg"] = () => Synth.Render(
            new Note(0, 110, 659, 659, SynthWave.Bell, 0.4, 9),
            new Note(110, 110, 784, 784, SynthWave.Bell, 0.4, 9),
            new Note(220, 300, 1047, 1047, SynthWave.Bell, 0.45, 6)),
        ["wow"] = () => Synth.Render(new Note(0, 260, 500, 1100, SynthWave.Sine, 0.5, 4)),
        ["sad"] = () => Synth.Render(new Note(0, 380, 523, 330, SynthWave.Triangle, 0.45, 3)),
        ["angry"] = () => Synth.Render(
            new Note(0, 90, 220, 180, SynthWave.Soft, 0.6, 6),
            new Note(110, 120, 220, 170, SynthWave.Soft, 0.6, 6)),
        ["sleepy"] = () => Synth.Render(new Note(0, 420, 392, 262, SynthWave.Sine, 0.4, 2)),
        ["hold"] = () => Synth.Render(
            new Note(0, 120, 587, 587, SynthWave.Triangle, 0.5, 6),
            new Note(140, 160, 587, 587, SynthWave.Triangle, 0.5, 6)),
        ["retreat"] = () => Synth.Render(
            new Note(0, 140, 784, 523, SynthWave.Soft, 0.45, 5),
            new Note(150, 180, 523, 392, SynthWave.Soft, 0.45, 5)),
        ["target"] = () => Synth.Render(
            new Note(0, 60, 1319, 1319, SynthWave.Bell, 0.4, 16),
            new Note(80, 220, 1760, 1760, SynthWave.Bell, 0.45, 10)),
    };

    /// <summary>Âm của gói âm thanh người dùng chọn (nếu có), không thì âm tổng hợp.</summary>
    public static float[] Get(string id) =>
        SoundPack.TryGet(id) ?? Cache.GetOrAdd(Recipes.ContainsKey(id) ? id : "pop", key => Recipes[key]());
}

/// <summary>Bộ trộn chạy liên tục: nhiều tín hiệu liên tiếp phát chồng lên nhau, không cắt nhau.</summary>
public sealed class SoundEngine : IDisposable
{
    private readonly WaveOutEvent? _out;
    private readonly MixingSampleProvider? _mixer;
    private readonly List<ISampleProvider> _clips = new();
    private readonly object _gate = new();

    public SoundEngine()
    {
        try
        {
            _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(Synth.Rate, 2)) { ReadFully = true };
            _out = new WaveOutEvent { DesiredLatency = 90, NumberOfBuffers = 3 };
            _out.Init(_mixer);
            _out.Play();
        }
        catch (Exception ex)
        {
            Log.Error("Audio init", ex);
            _out = null;
            _mixer = null;
        }
    }

    /// <summary>Phát âm tín hiệu/biểu cảm (luôn phát chồng).</summary>
    public void Play(string soundId, double volume01) => Add(Sounds.Get(soundId), volume01, clip: false);

    /// <summary>Phát âm trong kho âm thanh. replace = dừng các âm kho đang phát trước.</summary>
    public void PlayClip(float[] data, double volume01, bool replace)
    {
        if (replace) StopClips();
        Add(data, volume01, clip: true);
    }

    public void StopClips()
    {
        if (_mixer == null) return;
        lock (_gate)
        {
            foreach (var c in _clips) _mixer.RemoveMixerInput(c);
            _clips.Clear();
        }
    }

    public void StopAll()
    {
        if (_mixer == null) return;
        lock (_gate)
        {
            _mixer.RemoveAllMixerInputs();
            _clips.Clear();
        }
    }

    private void Add(float[] data, double volume01, bool clip)
    {
        if (_mixer == null || volume01 <= 0 || data.Length == 0) return;
        try
        {
            ISampleProvider p = new BufferProvider(data);
            // Cho phép tăng tới 200% cho âm meme nhỏ tiếng; bộ trộn tự kẹp biên độ khi xuất.
            p = new VolumeSampleProvider(p) { Volume = (float)Math.Clamp(volume01, 0, 2) };
            var stereo = new MonoToStereoSampleProvider(p);
            lock (_gate)
            {
                if (clip)
                {
                    // Âm đã phát xong tự rời bộ trộn; ở đây chỉ giữ danh sách gọn để còn dừng được.
                    if (_clips.Count > 64) _clips.RemoveRange(0, 32);
                    _clips.Add(stereo);
                }
                _mixer.AddMixerInput(stereo);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Audio play", ex);
        }
    }

    public void Dispose()
    {
        try { _out?.Stop(); _out?.Dispose(); } catch { }
    }

    private sealed class BufferProvider : ISampleProvider
    {
        private readonly float[] _data;
        private int _pos;

        public BufferProvider(float[] data) { _data = data; }

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Synth.Rate, 1);

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
