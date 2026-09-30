// Sound effects + haptics (user request 2026-09-27: "integrate audio sounds and
// haptics where it makes sense"). The game ships no audio files: every effect
// is synthesized once at startup (a few KB of samples each) — nothing to
// license or download. Haptics go through Plugins/iOS/GRHaptics.mm (Taptic
// Engine); elsewhere they're no-ops. Both can be switched off in the profile,
// and the sound follows the iPhone's silent switch (ambient audio session).
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace GalaxyRoyale.Game
{
    public enum Sfx
    {
        Tap, Open, Close, Toggle, Confirm, Success, Coins, Error, Alert,
        Launch, Laser, Explosion, Victory, Defeat, Quest,
        // The events and systems of 2026-09-30.
        Comet, Nova, Missile, Taunt, Discovery, Storm,
    }

    public enum Haptic { Selection, Light, Medium, Heavy, Success, Warning, Error }

    public sealed class GameAudio : MonoBehaviour
    {
        const string SoundKey = "galaxyroyale.sound";
        const string HapticsKey = "galaxyroyale.haptics";
        const int Voices = 8;

        static GameAudio? s_instance;
        static bool? s_soundOn, s_hapticsOn;
        readonly Dictionary<Sfx, AudioClip> _clips = new();
        readonly Dictionary<Sfx, float> _lastPlayed = new();
        readonly AudioSource[] _voices = new AudioSource[Voices];
        int _nextVoice;
        float _lastHaptic = -10f;

        public static bool SoundOn
        {
            get => s_soundOn ??= PlayerPrefs.GetInt(SoundKey, 1) == 1;
            set { s_soundOn = value; PlayerPrefs.SetInt(SoundKey, value ? 1 : 0); }
        }

        public static bool HapticsOn
        {
            get => s_hapticsOn ??= PlayerPrefs.GetInt(HapticsKey, 1) == 1;
            set { s_hapticsOn = value; PlayerPrefs.SetInt(HapticsKey, value ? 1 : 0); }
        }

        /// <summary>Play a sound; <paramref name="pitch"/> varies repeats (lasers, blasts).</summary>
        public static void Play(Sfx sfx, float volume = 1f, float pitch = 1f)
        {
            if (!SoundOn) return;
            var audio = Instance();
            if (audio == null) return;
            float now = Time.unscaledTime;
            if (audio._lastPlayed.TryGetValue(sfx, out var last) && now - last < MinGap(sfx)) return;
            audio._lastPlayed[sfx] = now;
            var voice = audio._voices[audio._nextVoice];
            audio._nextVoice = (audio._nextVoice + 1) % Voices;
            voice.pitch = pitch;
            voice.PlayOneShot(audio._clips[sfx], Mathf.Clamp01(volume * BaseVolume(sfx)));
        }

        /// <summary>A Taptic Engine tap (iOS device only). Throttled so bursts
        /// (a replay's blasts) stay a rumble, not a buzz.</summary>
        public static void Buzz(Haptic haptic)
        {
            if (!HapticsOn) return;
            var audio = Instance();
            if (audio == null) return;
            float now = Time.unscaledTime;
            if (now - audio._lastHaptic < 0.07f) return;
            audio._lastHaptic = now;
            NativeHaptics.Play(haptic);
        }

        public static void Feedback(Sfx sfx, Haptic haptic)
        {
            Play(sfx);
            Buzz(haptic);
        }

        /// <summary>Every button's click (Widgets) — a quiet tick, no haptic.</summary>
        public static void Tap() => Play(Sfx.Tap);

        static GameAudio? Instance()
        {
            if (s_instance != null) return s_instance;
            var go = new GameObject("Game Audio");
            DontDestroyOnLoad(go);
            return go.AddComponent<GameAudio>();
        }

        void Awake()
        {
            s_instance = this;
            for (int i = 0; i < Voices; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                _voices[i] = source;
            }
            foreach (Sfx sfx in Enum.GetValues(typeof(Sfx))) _clips[sfx] = Synth.Make(sfx);
        }

        void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
        }

        static float MinGap(Sfx sfx) => sfx switch
        {
            Sfx.Laser => 0.06f,
            Sfx.Explosion => 0.08f,
            Sfx.Tap => 0.04f,
            _ => 0.05f,
        };

        static float BaseVolume(Sfx sfx) => sfx switch
        {
            Sfx.Tap => 0.22f,
            Sfx.Toggle => 0.3f,
            Sfx.Open => 0.28f,
            Sfx.Close => 0.24f,
            Sfx.Laser => 0.16f,
            Sfx.Explosion => 0.42f,
            Sfx.Nova => 0.5f,
            Sfx.Storm => 0.4f,
            Sfx.Error => 0.35f,
            _ => 0.45f,
        };
    }

    /// <summary>The effects, built sample by sample (44.1 kHz mono).</summary>
    static class Synth
    {
        const int Rate = 44100;
        const float Tau = Mathf.PI * 2f;

        public static AudioClip Make(Sfx sfx) => sfx switch
        {
            Sfx.Tap => Tone("tap", 0.035f, t => 1500f, t => Decay(t, 0.001f, 0.008f), 0.25f),
            Sfx.Open => Tone("open", 0.13f, t => Mathf.Lerp(420f, 880f, t / 0.13f), t => Decay(t, 0.01f, 0.05f), 0.3f),
            Sfx.Close => Tone("close", 0.11f, t => Mathf.Lerp(820f, 400f, t / 0.11f), t => Decay(t, 0.005f, 0.045f), 0.25f),
            Sfx.Toggle => Tone("toggle", 0.06f, t => 1100f, t => Decay(t, 0.002f, 0.018f), 0.4f),
            Sfx.Confirm => Notes("confirm", new[] { 660f, 990f }, 0.06f, 0.12f),
            Sfx.Success => Notes("success", new[] { 784f, 988f, 1175f }, 0.075f, 0.28f),
            Sfx.Quest => Notes("quest", new[] { 1047f, 1319f, 1568f, 2093f }, 0.055f, 0.35f),
            Sfx.Coins => Coins(),
            Sfx.Error => Error(),
            Sfx.Alert => Alert(),
            Sfx.Launch => Launch(),
            Sfx.Laser => Tone("laser", 0.14f, t => Mathf.Lerp(2000f, 450f, Mathf.Sqrt(t / 0.14f)),
                t => Decay(t, 0.002f, 0.05f), 0.1f),
            Sfx.Explosion => Explosion(),
            Sfx.Victory => Notes("victory", new[] { 523f, 659f, 784f, 1047f }, 0.11f, 0.5f),
            Sfx.Defeat => Notes("defeat", new[] { 392f, 311f, 262f }, 0.17f, 0.55f, dark: true),
            Sfx.Comet => Comet(),
            Sfx.Nova => Nova(),
            Sfx.Missile => Missile(),
            Sfx.Taunt => Notes("taunt", new[] { 147f, 139f, 110f }, 0.14f, 0.6f, dark: true),
            // A rising, open fifth-and-octave figure: something found out there.
            Sfx.Discovery => Notes("discovery", new[] { 587f, 880f, 1175f, 1760f }, 0.09f, 0.6f),
            Sfx.Storm => Storm(),
            _ => Tone("blank", 0.01f, t => 0f, t => 0f, 0f),
        };

        static float Decay(float t, float attack, float decay) =>
            t < attack ? t / attack : Mathf.Exp(-(t - attack) / decay);

        static AudioClip Build(string name, float seconds, Func<int, float, float> sample)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(sample(i, i / (float)Rate), -1f, 1f);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>A (possibly sweeping) tone with a 2nd harmonic, phase-accumulated.</summary>
        static AudioClip Tone(string name, float seconds, Func<float, float> freq, Func<float, float> env, float harmonic)
        {
            float phase = 0f;
            return Build(name, seconds, (i, t) =>
            {
                phase += Tau * freq(t) / Rate;
                return (Mathf.Sin(phase) + harmonic * Mathf.Sin(2f * phase)) * env(t) * 0.8f;
            });
        }

        /// <summary>Overlapping plucked notes — chimes, fanfares, stings.</summary>
        static AudioClip Notes(string name, float[] freqs, float step, float tail, bool dark = false)
        {
            float seconds = step * (freqs.Length - 1) + tail;
            return Build(name, seconds, (i, t) =>
            {
                float sum = 0f;
                for (int k = 0; k < freqs.Length; k++)
                {
                    float local = t - k * step;
                    if (local < 0f) continue;
                    bool last = k == freqs.Length - 1;
                    float env = Decay(local, 0.004f, last ? tail * 0.45f : step * 1.6f);
                    float ph = Tau * freqs[k] * local;
                    float voice = Mathf.Sin(ph) + (dark ? 0.45f : 0.25f) * Mathf.Sin(2f * ph)
                        + (dark ? 0.2f : 0.08f) * Mathf.Sin(3f * ph);
                    sum += voice * env;
                }
                return sum * 0.45f;
            });
        }

        static AudioClip Coins() => Build("coins", 0.3f, (i, t) =>
        {
            float a = t < 0.09f ? Ping(t, 1568f) : 0f;
            float b = t >= 0.07f ? Ping(t - 0.07f, 2093f) : 0f;
            return (a + b) * 0.5f;
        });

        static float Ping(float t, float f) =>
            (Mathf.Sin(Tau * f * t) + 0.35f * Mathf.Sin(Tau * f * 2.76f * t)) * Decay(t, 0.002f, 0.06f);

        static AudioClip Error() => Build("error", 0.24f, (i, t) =>
        {
            float local = t < 0.11f ? t : t - 0.13f;
            if (local < 0f || local > 0.09f) return 0f;
            float ph = Tau * 165f * local;
            float square = Mathf.Sin(ph) + Mathf.Sin(3f * ph) / 3f + Mathf.Sin(5f * ph) / 5f;
            return square * Decay(local, 0.004f, 0.05f) * 0.55f;
        });

        static AudioClip Alert() => Build("alert", 0.62f, (i, t) =>
        {
            int seg = (int)(t / 0.155f);
            float local = t - seg * 0.155f;
            if (local > 0.12f) return 0f;
            float f = seg % 2 == 0 ? 740f : 988f;
            float tri = Mathf.Asin(Mathf.Sin(Tau * f * t)) * (2f / Mathf.PI);
            return tri * Decay(local, 0.006f, 0.1f) * 0.6f;
        });

        static AudioClip Launch()
        {
            var noise = new System.Random(11);
            float phase = 0f, low = 0f;
            return Build("launch", 0.6f, (i, t) =>
            {
                float k = t / 0.6f;
                phase += Tau * Mathf.Lerp(160f, 760f, k * k) / Rate;
                low += (((float)noise.NextDouble() * 2f - 1f) - low) * Mathf.Lerp(0.05f, 0.35f, k);
                float env = Mathf.Min(1f, t / 0.06f)
                    * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, k)));
                return (0.55f * Mathf.Sin(phase) + 0.45f * low) * env * 0.7f;
            });
        }

        /// <summary>A bright glissando with a shimmering tail: the comet streaks in.</summary>
        static AudioClip Comet()
        {
            float phase = 0f;
            return Build("comet", 0.9f, (i, t) =>
            {
                float k = t / 0.9f;
                phase += Tau * Mathf.Lerp(2400f, 900f, Mathf.Sqrt(k)) / Rate;
                float shimmer = 0.6f + 0.4f * Mathf.Sin(Tau * 18f * t);
                return (Mathf.Sin(phase) + 0.3f * Mathf.Sin(3f * phase)) * shimmer
                    * Decay(t, 0.02f, 0.3f) * 0.45f;
            });
        }

        /// <summary>A long, deep boom that swells before it breaks: the star goes off.</summary>
        static AudioClip Nova()
        {
            var noise = new System.Random(23);
            float low = 0f;
            return Build("nova", 1.6f, (i, t) =>
            {
                float swell = t < 0.35f ? t / 0.35f : Mathf.Exp(-(t - 0.35f) / 0.45f);
                low += (((float)noise.NextDouble() * 2f - 1f) - low) * Mathf.Lerp(0.2f, 0.02f, Mathf.Clamp01(t / 1.2f));
                float sub = Mathf.Sin(Tau * Mathf.Lerp(70f, 38f, Mathf.Clamp01(t / 1.6f)) * t);
                return (low * 1.5f + sub * 0.9f) * swell * 0.6f;
            });
        }

        /// <summary>A falling whistle, then a sharp crack: the silo's salvo lands.</summary>
        static AudioClip Missile()
        {
            var noise = new System.Random(5);
            float phase = 0f, low = 0f;
            return Build("missile", 0.7f, (i, t) =>
            {
                if (t < 0.4f)
                {
                    phase += Tau * Mathf.Lerp(1800f, 700f, t / 0.4f) / Rate;
                    return Mathf.Sin(phase) * Mathf.Min(1f, t / 0.05f) * 0.3f;
                }
                float local = t - 0.4f;
                low += (((float)noise.NextDouble() * 2f - 1f) - low) * 0.5f;
                return (low * 1.2f + Mathf.Sin(Tau * 80f * local) * 0.6f) * Decay(local, 0.002f, 0.08f) * 0.7f;
            });
        }

        /// <summary>Crackling static over a low hum: the ion storm rolls in.</summary>
        static AudioClip Storm()
        {
            var noise = new System.Random(31);
            return Build("storm", 1.1f, (i, t) =>
            {
                float env = Mathf.Min(1f, t / 0.15f) * Mathf.Exp(-t / 0.55f);
                float crackle = noise.NextDouble() < 0.012 ? (float)noise.NextDouble() * 2f - 1f : 0f;
                float hum = Mathf.Sin(Tau * 60f * t) + 0.5f * Mathf.Sin(Tau * 120f * t + Mathf.Sin(Tau * 3f * t));
                return (crackle * 1.6f + hum * 0.35f) * env * 0.6f;
            });
        }

        static AudioClip Explosion()
        {
            var noise = new System.Random(7);
            float low = 0f;
            return Build("explosion", 0.62f, (i, t) =>
            {
                float cutoff = Mathf.Lerp(0.45f, 0.03f, Mathf.Clamp01(t / 0.5f));
                low += (((float)noise.NextDouble() * 2f - 1f) - low) * cutoff;
                float rumble = low * Decay(t, 0.003f, 0.17f);
                float thump = Mathf.Sin(Tau * 55f * t) * Decay(t, 0.002f, 0.1f);
                return (rumble * 1.4f + thump * 0.8f) * 0.7f;
            });
        }
    }

    /// <summary>Taptic Engine bridge (Plugins/iOS/GRHaptics.mm).</summary>
    static class NativeHaptics
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void _GRHapticImpact(int style);
        [DllImport("__Internal")] static extern void _GRHapticNotify(int type);
        [DllImport("__Internal")] static extern void _GRHapticSelection();
#endif

        public static void Play(Haptic haptic)
        {
#if UNITY_IOS && !UNITY_EDITOR
            switch (haptic)
            {
                case Haptic.Selection: _GRHapticSelection(); break;
                case Haptic.Light: _GRHapticImpact(0); break;
                case Haptic.Medium: _GRHapticImpact(1); break;
                case Haptic.Heavy: _GRHapticImpact(2); break;
                case Haptic.Success: _GRHapticNotify(0); break;
                case Haptic.Warning: _GRHapticNotify(1); break;
                case Haptic.Error: _GRHapticNotify(2); break;
            }
#endif
        }
    }
}
