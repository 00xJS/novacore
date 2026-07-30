// Procedural placeholder art for the galaxy map: discs, glows, rings, starfield.
// All sprites are generated once and cached — same philosophy as the planet
// texture generator (no PNG assets until the user sources real art).
using UnityEngine;

namespace GalaxyRoyale.Game
{
    public static class MapVisuals
    {
        static Sprite? _disc;
        static Sprite? _glow;
        static Sprite? _ring;
        static Sprite? _bubble;
        static Sprite? _ember;
        static Texture2D? _stars;

        /// <summary>Filled antialiased circle, 1 world-unit diameter at scale 1.</summary>
        public static Sprite Disc
        {
            get
            {
                if (_disc != null) return _disc;
                const int size = 64;
                var tex = NewTex(size);
                float r = size * 0.5f - 1f;
                FillRadial(tex, size, d => Mathf.Clamp01(r - d + 0.5f));
                _disc = MakeSprite(tex, size);
                return _disc;
            }
        }

        /// <summary>Soft radial glow, alpha falls off with (1-d)^2.4.</summary>
        public static Sprite Glow
        {
            get
            {
                if (_glow != null) return _glow;
                const int size = 128;
                var tex = NewTex(size);
                float r = size * 0.5f;
                FillRadial(tex, size, d =>
                {
                    float t = Mathf.Clamp01(1f - d / r);
                    return Mathf.Pow(t, 2.4f);
                });
                _glow = MakeSprite(tex, size);
                return _glow;
            }
        }

        /// <summary>Thin annulus at the sprite edge — tier rings, orbit rings, select reticle.</summary>
        public static Sprite Ring
        {
            get
            {
                if (_ring != null) return _ring;
                const int size = 256;
                var tex = NewTex(size);
                float outer = size * 0.5f - 1f;
                const float thickness = 3f;
                FillRadial(tex, size, d =>
                {
                    float edge = Mathf.Abs(d - (outer - thickness));
                    return Mathf.Clamp01(thickness - edge);
                });
                _ring = MakeSprite(tex, size);
                return _ring;
            }
        }

        /// <summary>
        /// Energy shield bubble: a solid translucent dome fill with a bright rim
        /// band and a faint inner refraction ring — reads as a force field, not a
        /// blob, once tinted. Built like the flame: procedural, no art asset.
        /// </summary>
        public static Sprite Bubble
        {
            get
            {
                if (_bubble != null) return _bubble;
                const int size = 256;
                var tex = NewTex(size);
                float r = size * 0.5f - 1f;
                FillRadial(tex, size, d =>
                {
                    float t = d / r;                    // 0 center → 1 edge
                    if (t >= 1f) return 0f;
                    float fill = 0.34f;                 // solid dome interior
                    // Bright rim band right at the edge (gaussian around t≈0.93).
                    float rim = Mathf.Exp(-Mathf.Pow((t - 0.93f) * 16f, 2f)) * 0.85f;
                    // Faint inner "refraction" ring gives the dome depth.
                    float inner = Mathf.Exp(-Mathf.Pow((t - 0.74f) * 20f, 2f)) * 0.16f;
                    float cut = Mathf.Clamp01((1f - t) * 14f); // antialiased outer edge
                    return Mathf.Clamp01(fill + rim + inner) * cut;
                });
                _bubble = MakeSprite(tex, size);
                return _bubble;
            }
        }

        /// <summary>Hot ember particle sprite: a tight bright core inside a soft
        /// falloff — crisper than the generic Glow, so flames read as fire rather
        /// than fog at close zoom.</summary>
        public static Sprite Ember
        {
            get
            {
                if (_ember != null) return _ember;
                const int size = 64;
                var tex = NewTex(size);
                float r = size * 0.5f;
                FillRadial(tex, size, d =>
                {
                    float t = Mathf.Clamp01(1f - d / r);
                    float halo = Mathf.Pow(t, 2.1f) * 0.75f;
                    float core = Mathf.Pow(t, 7f); // hot center
                    return Mathf.Clamp01(halo + core);
                });
                _ember = MakeSprite(tex, size);
                return _ember;
            }
        }

        /// <summary>Tileable starfield texture (wrap = Repeat) for parallax backdrop quads.</summary>
        public static Texture2D Stars
        {
            get
            {
                if (_stars != null) return _stars;
                const int size = 512;
                var tex = NewTex(size);
                var rng = new System.Random(1234);
                var px = new Color[size * size];
                for (int i = 0; i < 380; i++)
                {
                    int x = rng.Next(size);
                    int y = rng.Next(size);
                    float b = 0.25f + (float)rng.NextDouble() * 0.75f;
                    px[y * size + x] = new Color(b, b, Mathf.Min(1f, b * 1.1f), b);
                    if (b > 0.85f) // bright stars get a tiny cross bloom
                    {
                        var half = new Color(b, b, b, b * 0.4f);
                        px[y * size + (x + 1) % size] = half;
                        px[y * size + (x + size - 1) % size] = half;
                        px[((y + 1) % size) * size + x] = half;
                        px[((y + size - 1) % size) * size + x] = half;
                    }
                }
                tex.SetPixels(px);
                tex.Apply();
                tex.wrapMode = TextureWrapMode.Repeat;
                _stars = tex;
                return _stars;
            }
        }

        /// <summary>
        /// Living flame for a burning planet: ember particles spawn on the rim,
        /// rise screen-up (LOCAL simulation space rides the parent's billboard, so
        /// "up" stays up at any camera tilt), shrink, and cool white-yellow →
        /// orange → deep red before fading. Fully procedural — additive-blended
        /// soft-glow particles, no art asset involved.
        /// </summary>
        public static ParticleSystem FireParticles(Transform parent, int sortingOrder)
        {
            // Layer 1 — the body of the fire: bigger, denser tongues of flame
            // (2026-07-07 upgrade: user wanted bigger & more flames). Ember sprite
            // (hot core + soft halo) + turbulence noise + slow tumble replace the
            // old foggy Glow puffs.
            var go = new GameObject("Flames");
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = Vector3.zero;
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy; // zoom scales the fire
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.24f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.26f, 0.62f); // planet disc = 1 unit
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.88f, 0.40f, 0.95f),  // hot yellow
                new Color(1f, 0.45f, 0.10f, 0.95f)); // orange
            main.maxParticles = 80;
            main.playOnAwake = true; // (re)ignites whenever the Burn root activates

            var emission = ps.emission;
            emission.rateOverTime = 42f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.46f;
            shape.radiusThickness = 0.40f; // bias spawns toward the rim — licking edges

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.y = new ParticleSystem.MinMaxCurve(0.40f, 0.85f); // flames rise

            // Turbulence makes the tongues waver instead of rising in straight lanes.
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(0.10f, 0.18f);
            noise.frequency = 1.7f;
            noise.scrollSpeed = 0.7f;
            noise.damping = true;

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.9f, 0.9f); // slow tumble (rad/s)

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.97f, 0.72f), 0f),   // near-white heart
                    new GradientColorKey(new Color(1f, 0.62f, 0.15f), 0.30f),
                    new GradientColorKey(new Color(0.95f, 0.28f, 0.06f), 0.65f),
                    new GradientColorKey(new Color(0.42f, 0.06f, 0.05f), 1f), // dying red
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),      // fade in — no popping
                    new GradientAlphaKey(0.95f, 0.12f),
                    new GradientAlphaKey(0.50f, 0.7f),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = grad;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 0.75f), new Keyframe(0.2f, 1f),
                    new Keyframe(1f, 0.12f))); // swell, then shrink to an ember

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = AdditiveParticleMat(Ember.texture);
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingOrder = sortingOrder;

            // Layer 2 — sparks: tiny, fast, bright motes thrown clear of the blaze.
            // Cheap (≤26 particles) but sells the violence of the fire.
            var sparksGo = new GameObject("Sparks");
            sparksGo.transform.SetParent(parent, worldPositionStays: false);
            sparksGo.transform.localPosition = Vector3.zero;
            var sparks = sparksGo.AddComponent<ParticleSystem>();

            var sMain = sparks.main;
            sMain.simulationSpace = ParticleSystemSimulationSpace.Local;
            sMain.scalingMode = ParticleSystemScalingMode.Hierarchy;
            sMain.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.0f);
            sMain.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
            sMain.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            sMain.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.95f, 0.70f, 1f), new Color(1f, 0.70f, 0.25f, 1f));
            sMain.maxParticles = 26;
            sMain.playOnAwake = true;

            var sEmit = sparks.emission;
            sEmit.rateOverTime = 15f;

            var sShape = sparks.shape;
            sShape.shapeType = ParticleSystemShapeType.Circle;
            sShape.radius = 0.44f;
            sShape.radiusThickness = 0.25f;

            var sVel = sparks.velocityOverLifetime;
            sVel.enabled = true;
            sVel.space = ParticleSystemSimulationSpace.Local;
            sVel.y = new ParticleSystem.MinMaxCurve(0.9f, 1.6f); // sparks fly high

            var sNoise = sparks.noise;
            sNoise.enabled = true;
            sNoise.strength = new ParticleSystem.MinMaxCurve(0.25f, 0.45f); // jittery paths
            sNoise.frequency = 3f;

            var sCol = sparks.colorOverLifetime;
            sCol.enabled = true;
            var sGrad = new Gradient();
            sGrad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.95f, 0.75f), 0f),
                    new GradientColorKey(new Color(1f, 0.55f, 0.15f), 0.6f),
                    new GradientColorKey(new Color(0.7f, 0.15f, 0.05f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.8f, 0.6f),
                    new GradientAlphaKey(0f, 1f),
                });
            sCol.color = sGrad;

            var sRenderer = sparksGo.GetComponent<ParticleSystemRenderer>();
            sRenderer.material = AdditiveParticleMat(Ember.texture);
            sRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            sRenderer.sortingOrder = sortingOrder + 1; // sparks over the flames

            return ps;
        }

        /// <summary>Additive-blended particle material. The blend factors are plain
        /// material FLOATS (not shader keywords), so this runtime edit is safe
        /// against the device-build keyword-stripping trap.</summary>
        static Material AdditiveParticleMat(Texture tex)
        {
            var mat = UnlitTransparent(tex);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            return mat;
        }

        /// <summary>A SpriteRenderer child under `parent`, positioned/scaled in world units.</summary>
        public static SpriteRenderer Spawn(
            Transform? parent, string name, Sprite sprite, Vector3 localPos,
            float scaleX, float scaleY, Color color, int sortingOrder)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPos;
            go.transform.localScale = new Vector3(scaleX, scaleY, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = sortingOrder;
            return sr;
        }

        // ---------- user-art override hooks (like the building-art pipeline) ----------
        // Drop PNGs into Assets/Resources/Map/ and these names light up, replacing
        // the procedural sprites: planet-default / planet-crimson / planet-emerald,
        // node-asteroid / node-nebula / node-heliumcloud / node-derelict / node-camp,
        // fleet, probe. Missing files fall back to the generated art.
        static readonly System.Collections.Generic.Dictionary<string, Sprite?> _artOverrides = new();

        public static Sprite? OverrideSprite(string name)
        {
            if (_artOverrides.TryGetValue(name, out var cached)) return cached;
            var tex = UnityEngine.Resources.Load<Texture2D>($"Map/{name}");
            var sprite = tex != null
                ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f), tex.width) // 1 world unit at scale 1
                : null;
            _artOverrides[name] = sprite;
            return sprite;
        }

        // Materials come from template assets in Resources/Materials (created by
        // MaterialFactory in the editor). Shader.Find alone breaks in device
        // builds: shaders only ship when some built asset references them, and
        // all our materials are runtime-created — the templates ARE that asset,
        // carrying the shader + the exact keyword variants we clone.
        //
        // Shader.Find fallbacks are kept so the editor still works before the
        // factory has run.

        /// <summary>Unlit transparent material for mesh quads (starfield layers).</summary>
        public static Material UnlitTransparent(Texture tex)
        {
            var template = UnityEngine.Resources.Load<Material>("Materials/NovaUnlitTransparent");
            if (template != null) return new Material(template) { mainTexture = tex };

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var mat = new Material(shader!);
            mat.mainTexture = tex;
            ConfigureTransparent(mat);
            return mat;
        }

        /// <summary>Bake URP "Surface Type: Transparent" into a material. No-op for non-URP shaders.</summary>
        public static void ConfigureTransparent(Material mat)
        {
            if (!mat.HasProperty("_Surface")) return;
            mat.SetFloat("_Surface", 1f); // transparent
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        /// <summary>Fresh URP/Lit opaque material, or null if neither template nor shader exists.</summary>
        public static Material? LitOpaque()
        {
            var template = UnityEngine.Resources.Load<Material>("Materials/NovaLit");
            if (template != null) return new Material(template);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            return shader != null ? new Material(shader) : null;
        }

        /// <summary>Fresh Sprites/Default material (march lines), or null if unavailable.</summary>
        public static Material? SpriteDefault()
        {
            var template = UnityEngine.Resources.Load<Material>("Materials/NovaSprites");
            if (template != null) return new Material(template);
            var shader = Shader.Find("Sprites/Default");
            return shader != null ? new Material(shader) : null;
        }

        static Texture2D NewTex(int size) =>
            new(size, size, TextureFormat.RGBA32, mipChain: false) { filterMode = FilterMode.Bilinear };

        /// <summary>Fill a square texture where alpha = f(distance from center in px); rgb = white.</summary>
        static void FillRadial(Texture2D tex, int size, System.Func<float, float> alphaOf)
        {
            var px = new Color[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float a = Mathf.Clamp01(alphaOf(d));
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(px);
            tex.Apply();
        }

        /// <summary>Sprite spanning exactly 1 world unit at scale 1 (ppu = texture size).</summary>
        static Sprite MakeSprite(Texture2D tex, int size) =>
            Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                pixelsPerUnit: size, extrude: 0, meshType: SpriteMeshType.FullRect);
    }
}
