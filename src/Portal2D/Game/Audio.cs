using System.Numerics;
using Raylib_cs;

namespace Portal2D.Game;

/// <summary>All sound effects are synthesized at startup - no audio files needed.</summary>
public sealed class Audio
{
    const int SR = 44100;
    readonly Dictionary<Sfx, Sound[]> voices = new();
    readonly Dictionary<Sfx, int> next = new();
    readonly List<Sound> owned = new();
    Sound uiHover, uiClick, ambient;
    readonly Random rng = new(7);
    public bool Ready;
    public float Master = 0.8f, SfxVolume = 1f, AmbienceVolume = 0.5f;

    public void Load()
    {
        Raylib.InitAudioDevice();
        if (!Raylib.IsAudioDeviceReady()) return;
        Ready = true;

        Add(Sfx.PortalBlue, PortalShot(320, 980));
        Add(Sfx.PortalOrange, PortalShot(230, 700));
        Add(Sfx.PortalFail, Gen(0.2f, (t, i) => (Square(140 - t * 220, t) * 0.4f + Noise() * 0.3f) * Env(t, 0.005f, 0.2f)));
        Add(Sfx.Jump, Gen(0.14f, (t, i) => Sine(Sweep(280, 520, t, 0.14f), t) * Env(t, 0.004f, 0.14f) * 0.35f));
        Add(Sfx.Land, LowNoise(0.14f, 0.5f, 90, 45));
        Add(Sfx.Teleport, Whoosh());
        Add(Sfx.ButtonDown, Gen(0.18f, (t, i) => (Noise() * Env(t, 0.001f, 0.015f) * 0.6f + Sine(520, t) * Env(t, 0.005f, 0.18f) * 0.35f)));
        Add(Sfx.ButtonUp, Gen(0.14f, (t, i) => (Noise() * Env(t, 0.001f, 0.012f) * 0.5f + Sine(360, t) * Env(t, 0.005f, 0.14f) * 0.3f)));
        Add(Sfx.DoorOpen, Door(true));
        Add(Sfx.DoorClose, Door(false));
        Add(Sfx.Pickup, Gen(0.12f, (t, i) => Sine(Sweep(600, 950, t, 0.12f), t) * Env(t, 0.004f, 0.12f) * 0.3f));
        Add(Sfx.Drop, Gen(0.12f, (t, i) => Sine(Sweep(520, 300, t, 0.12f), t) * Env(t, 0.004f, 0.12f) * 0.3f));
        Add(Sfx.Death, Gen(0.8f, (t, i) => (Saw(Sweep(320, 50, t, 0.8f), t) * 0.35f + Noise() * 0.25f * (1 - t / 0.8f)) * Env(t, 0.005f, 0.8f)));
        Add(Sfx.Win, Win());
        Add(Sfx.Fizzle, Gen(0.45f, (t, i) => (Noise() * (rng.NextDouble() < 0.15 ? 0.7f : 0.1f) + Sine(Sweep(1400, 2200, t, 0.45f), t) * 0.15f) * Env(t, 0.005f, 0.45f)));
        Add(Sfx.CubeDissolve, Gen(0.7f, (t, i) => (Sine(800 + t * 900, t) + Sine(1210 + t * 700, t) * 0.6f) * 0.2f * (0.6f + 0.4f * MathF.Sin(t * 70)) * Env(t, 0.02f, 0.7f)));
        Add(Sfx.CubeSpawn, Gen(0.25f, (t, i) => Sine(Sweep(380, 720, t, 0.25f), t) * Env(t, 0.01f, 0.25f) * 0.3f));
        Add(Sfx.ReceiverOn, Gen(0.5f, (t, i) => (Sine(Sweep(180, 260, t, 0.5f), t) + Sine(Sweep(360, 520, t, 0.5f), t) * 0.5f) * Env(t, 0.03f, 0.5f) * 0.3f));

        uiHover = Make(Gen(0.05f, (t, i) => Sine(1100, t) * Env(t, 0.002f, 0.05f) * 0.12f));
        uiClick = Make(Gen(0.1f, (t, i) => Sine(Sweep(700, 1200, t, 0.1f), t) * Env(t, 0.002f, 0.1f) * 0.25f));
        ambient = Make(Ambience());
        ApplyVolumes();
    }

    public void Unload()
    {
        if (!Ready) return;
        foreach (var v in voices.Values) for (int i = 1; i < v.Length; i++) Raylib.UnloadSoundAlias(v[i]);
        foreach (var s in owned) Raylib.UnloadSound(s);
        Raylib.CloseAudioDevice();
    }

    public void ApplyVolumes()
    {
        if (!Ready) return;
        Raylib.SetMasterVolume(Master);
        Raylib.SetSoundVolume(ambient, AmbienceVolume * 0.6f);
    }

    public void Update()
    {
        if (!Ready) return;
        if (!Raylib.IsSoundPlaying(ambient)) Raylib.PlaySound(ambient);
    }

    public void Play(Sfx s, Vector2 pos, Vector2 listener)
    {
        if (!Ready || !voices.TryGetValue(s, out var v)) return;
        int idx = next[s];
        next[s] = (idx + 1) % v.Length;
        float dist = Vector2.Distance(pos, listener);
        float vol = Math.Clamp(1.15f - dist / 1300f, 0.35f, 1f) * SfxVolume;
        Raylib.SetSoundVolume(v[idx], vol);
        Raylib.SetSoundPitch(v[idx], 1 + ((float)rng.NextDouble() - 0.5f) * 0.08f);
        Raylib.SetSoundPan(v[idx], Math.Clamp(0.5f - (pos.X - listener.X) / 2400f, 0.15f, 0.85f));
        Raylib.PlaySound(v[idx]);
    }

    public void Hover() { if (Ready) { Raylib.SetSoundVolume(uiHover, SfxVolume); Raylib.PlaySound(uiHover); } }
    public void Click() { if (Ready) { Raylib.SetSoundVolume(uiClick, SfxVolume); Raylib.PlaySound(uiClick); } }

    // ------------------------------------------------------------------ synthesis

    void Add(Sfx s, float[] data)
    {
        var baseSound = Make(data);
        var arr = new Sound[3];
        arr[0] = baseSound;
        for (int i = 1; i < arr.Length; i++) arr[i] = Raylib.LoadSoundAlias(baseSound);
        voices[s] = arr;
        next[s] = 0;
    }

    unsafe Sound Make(float[] data)
    {
        var pcm = new short[data.Length];
        for (int i = 0; i < data.Length; i++) pcm[i] = (short)(Math.Clamp(data[i], -1f, 1f) * 32000);
        fixed (short* p = pcm)
        {
            var wave = new Wave { FrameCount = (uint)pcm.Length, SampleRate = SR, SampleSize = 16, Channels = 1, Data = p };
            var s = Raylib.LoadSoundFromWave(wave);
            owned.Add(s);
            return s;
        }
    }

    // each oscillator call inside a Gen() lambda gets its own phase accumulator (by call order)
    readonly List<double> phases = new();
    int oscIndex;
    float Osc(float f)
    {
        if (oscIndex >= phases.Count) phases.Add(0);
        double p = phases[oscIndex] + f / SR * Math.Tau;
        phases[oscIndex++] = p;
        return (float)(p % Math.Tau);
    }
    float Sine(float f, float t) => MathF.Sin(Osc(f));
    float Square(float f, float t) => MathF.Sin(Osc(f)) > 0 ? 1 : -1;
    float Saw(float f, float t) { float p = Osc(f) / MathF.Tau; return 2 * (p - MathF.Floor(p + 0.5f)); }

    float Noise() => (float)rng.NextDouble() * 2 - 1;
    static float Sweep(float a, float b, float t, float dur) => a * MathF.Pow(b / a, Math.Clamp(t / dur, 0, 1));
    static float Env(float t, float attack, float dur)
    {
        if (t < attack) return t / attack;
        float d = (t - attack) / MathF.Max(0.0001f, dur - attack);
        return MathF.Max(0, MathF.Pow(1 - MathF.Min(1, d), 2.2f));
    }

    /// <summary>Generic generator. Oscillator calls inside <paramref name="f"/> each get their own phase.</summary>
    float[] Gen(float dur, Func<float, int, float> f)
    {
        int n = (int)(dur * SR);
        var data = new float[n];
        phases.Clear();
        for (int i = 0; i < n; i++)
        {
            oscIndex = 0;
            data[i] = f(i / (float)SR, i);
        }
        return data;
    }

    float[] PortalShot(float f0, float f1)
    {
        const float dur = 0.42f;
        int n = (int)(dur * SR);
        var d = new float[n];
        double p1 = 0, p2 = 0;
        float lp = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float f = Sweep(f0, f1, t, 0.18f) * (1 + 0.03f * MathF.Sin(t * 90));
            p1 += f / SR * Math.Tau; p2 += f * 1.5f / SR * Math.Tau;
            lp += (Noise() - lp) * 0.2f;
            float s = MathF.Sin((float)p1) * 0.45f + MathF.Sin((float)p2) * 0.2f + lp * 0.5f * Env(t, 0.001f, 0.08f);
            d[i] = s * Env(t, 0.003f, dur) * 0.8f;
        }
        return d;
    }

    float[] LowNoise(float dur, float amp, float f0, float f1)
    {
        int n = (int)(dur * SR);
        var d = new float[n];
        float lp = 0; double p = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            lp += (Noise() - lp) * 0.05f;
            p += Sweep(f0, f1, t, dur) / SR * Math.Tau;
            d[i] = (lp * 1.5f + MathF.Sin((float)p) * 0.5f) * amp * Env(t, 0.002f, dur);
        }
        return d;
    }

    float[] Whoosh()
    {
        const float dur = 0.4f;
        int n = (int)(dur * SR);
        var d = new float[n];
        float lp = 0, bp = 0; double p = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float cut = 0.02f + 0.25f * MathF.Sin(MathF.PI * t / dur);
            lp += (Noise() - lp) * cut;
            bp += (lp - bp) * 0.5f;
            p += Sweep(700, 180, t, dur) / SR * Math.Tau;
            d[i] = (bp * 0.8f + MathF.Sin((float)p) * 0.25f) * MathF.Sin(MathF.PI * t / dur) * 0.7f;
        }
        return d;
    }

    float[] Door(bool open)
    {
        const float dur = 0.55f;
        int n = (int)(dur * SR);
        var d = new float[n];
        float lp = 0; double p = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            lp += (Noise() - lp) * 0.04f;
            p += (open ? Sweep(90, 160, t, dur) : Sweep(160, 80, t, dur)) / SR * Math.Tau;
            float clunk = t > dur - 0.08f ? Noise() * Env(t - (dur - 0.08f), 0.001f, 0.08f) * 0.5f : 0;
            d[i] = (lp * 2.2f + MathF.Sin((float)p) * 0.3f) * 0.45f * MathF.Min(1, t * 30) * (1 - t / dur * 0.5f) + clunk;
        }
        return d;
    }

    float[] Win()
    {
        float[] notes = [523.25f, 659.25f, 783.99f, 1046.5f];
        const float dur = 1.1f;
        int n = (int)(dur * SR);
        var d = new float[n];
        for (int k = 0; k < notes.Length; k++)
        {
            float start = k * 0.09f;
            for (int i = (int)(start * SR); i < n; i++)
            {
                float t = i / (float)SR - start;
                float s = MathF.Sin(MathF.Tau * notes[k] * t) * 0.6f + MathF.Sin(MathF.Tau * notes[k] * 2 * t) * 0.15f;
                d[i] += s * Env(t, 0.005f, dur - start) * 0.22f;
            }
        }
        return d;
    }

    float[] Ambience()
    {
        const float dur = 6f;
        int n = (int)(dur * SR);
        var d = new float[n];
        float lp = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            lp += (Noise() - lp) * 0.01f;
            float lfo = 0.6f + 0.4f * MathF.Sin(MathF.Tau * t / dur);
            float s = MathF.Sin(MathF.Tau * 55 * t) * 0.35f + MathF.Sin(MathF.Tau * 82.5f * t) * 0.2f * lfo
                    + MathF.Sin(MathF.Tau * 110 * t) * 0.08f + lp * 0.9f;
            d[i] = s * 0.35f;
        }
        return d;
    }
}
