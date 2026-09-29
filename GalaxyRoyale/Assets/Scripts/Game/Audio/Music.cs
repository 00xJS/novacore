// Ambient music (build-all plan, 2026-09-28) — like the sound effects, made
// on the fly rather than shipped: a slow pad over a minor progression (Am, F,
// C, G, eight seconds each, crossfading), a soft sub bass, and a sparse
// plucked arpeggio from the chord, all through a gentle echo. It's generated
// sample by sample on the audio thread (OnAudioFilterRead), so there is no
// loop seam and nothing to download. Settings › Music switches it off or sets
// its volume; it follows the silent switch with the rest of the game's audio.
using System;
using UnityEngine;

namespace GalaxyRoyale.Game
{
    public sealed class Music : MonoBehaviour
    {
        static Music? s_instance;

        /// <summary>Start (or stop) the music to match the settings.</summary>
        public static void Sync()
        {
            if (!Settings.MusicOn)
            {
                if (s_instance != null) s_instance._target = 0f;
                return;
            }
            if (s_instance == null)
            {
                var go = new GameObject("Music");
                DontDestroyOnLoad(go);
                s_instance = go.AddComponent<Music>();
            }
            s_instance._target = Settings.MusicVolume;
        }

        // Chords as MIDI notes (A minor: Am, F, C, G) — pad voicing plus the bass root.
        static readonly int[][] Chords =
        {
            new[] { 57, 60, 64 }, // A3 C4 E4
            new[] { 53, 57, 60 }, // F3 A3 C4
            new[] { 55, 60, 64 }, // G3 C4 E4 (C over G)
            new[] { 55, 59, 62 }, // G3 B3 D4
        };
        static readonly int[] Roots = { 45, 41, 48, 43 }; // A2 F2 C3 G2
        static readonly double[][] ChordFreqs = Array.ConvertAll(Chords, c => Array.ConvertAll(c, Freq));
        static readonly double[] RootFreqs = Array.ConvertAll(Roots, Freq);
        const double ChordSec = 8, FadeSec = 2.5;
        const float MasterGain = 0.25f;

        int _rate;
        double _time;
        volatile float _target;
        float _gain;
        readonly double[] _padPhase = new double[6];
        double _bassPhase;
        // Arpeggio: one pluck at a time.
        double _pluckFreq, _pluckStart = -10, _pluckPhase, _nextPluck;
        uint _rng = 0x9E3779B9u;
        // Echo.
        float[] _delay = Array.Empty<float>();
        int _delayPos;

        void Awake()
        {
            _rate = AudioSettings.outputSampleRate;
            _delay = new float[Mathf.Max(1, (int)(_rate * 0.42))];
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.loop = true;
            // A silent looping clip keeps the source (and so OnAudioFilterRead) running.
            source.clip = AudioClip.Create("music-carrier", _rate, 1, _rate, false);
            source.Play();
        }

        void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
        }

        static double Freq(int midi) => 440.0 * Math.Pow(2, (midi - 69) / 12.0);

        uint Next()
        {
            _rng ^= _rng << 13;
            _rng ^= _rng >> 17;
            _rng ^= _rng << 5;
            return _rng;
        }

        /// <summary>Weight of chord <paramref name="index"/> at time t: 1 inside its slot,
        /// crossfading over FadeSec at the edges.</summary>
        static double ChordWeight(double t, int index)
        {
            double cycle = ChordSec * Chords.Length;
            double local = ((t - index * ChordSec) % cycle + cycle) % cycle; // 0 = its slot starts
            if (local < ChordSec) return local < FadeSec ? local / FadeSec : 1;
            double after = local - ChordSec;
            return after < FadeSec ? 1 - after / FadeSec : 0;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (_rate <= 0) return;
            double dt = 1.0 / _rate;
            for (int i = 0; i < data.Length; i += channels)
            {
                // Glide toward the target volume (fades in and out, never clicks).
                _gain += (_target - _gain) * 0.00005f;
                double t = _time;
                double sample = 0;

                if (_gain > 0.0005f)
                {
                    // Pad: each chord's three notes, slightly detuned pairs, soft harmonics.
                    for (int c = 0; c < Chords.Length; c++)
                    {
                        double w = ChordWeight(t, c);
                        if (w <= 0) continue;
                        for (int n = 0; n < 3; n++)
                        {
                            double f = ChordFreqs[c][n];
                            double a = Math.Sin(2 * Math.PI * f * t) + 0.3 * Math.Sin(2 * Math.PI * f * 2.003 * t)
                                + 0.12 * Math.Sin(2 * Math.PI * f * 3.01 * t);
                            sample += a * w * 0.09 * (1 + 0.15 * Math.Sin(2 * Math.PI * 0.07 * t + n));
                        }
                        double root = RootFreqs[c];
                        sample += Math.Sin(2 * Math.PI * root * t) * w * 0.16;
                    }

                    // Arpeggio: a pluck every 0.75 s on the current chord (some rests).
                    if (t >= _nextPluck)
                    {
                        int chord = (int)(t / ChordSec) % Chords.Length;
                        _nextPluck = t + 0.75;
                        if (Next() % 4 != 0)
                        {
                            int note = Chords[chord][Next() % 3] + 12 * (1 + (int)(Next() % 2));
                            _pluckFreq = Freq(note);
                            _pluckStart = t;
                            _pluckPhase = 0;
                        }
                    }
                    double since = t - _pluckStart;
                    if (since < 2.5)
                    {
                        _pluckPhase += 2 * Math.PI * _pluckFreq * dt;
                        double env = since < 0.01 ? since / 0.01 : Math.Exp(-(since - 0.01) / 0.55);
                        sample += (Math.Sin(_pluckPhase) + 0.2 * Math.Sin(2 * _pluckPhase)) * env * 0.12;
                    }
                }

                // Echo: 0.42 s delay, soft feedback.
                float dry = (float)sample * _gain * MasterGain;
                float wet = _delay[_delayPos];
                _delay[_delayPos] = dry + wet * 0.38f;
                _delayPos = (_delayPos + 1) % _delay.Length;
                float o = dry + wet * 0.45f;
                for (int ch = 0; ch < channels; ch++) data[i + ch] += o;
                _time += dt; // a double: sample-accurate for days of play
            }
        }
    }
}
