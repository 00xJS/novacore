// Programmatically wires the initial Base-scene visual stack: Camera, Directional
// Light, and a placeholder home-planet sphere. Kept in code so the scene file
// stays minimal — Phase B.3 refactors this into proper prefabs + scene hierarchy
// once we know the final layout.
using System;
using System.IO;
using UnityEngine;

namespace GalaxyRoyale.Game
{
    [AddComponentMenu("GalaxyRoyale/Scene Bootstrap")]
    [RequireComponent(typeof(GameContext))]
    public sealed class SceneBootstrap : MonoBehaviour
    {
        [Header("Planet")]
        [SerializeField] float planetRadius = 3.5f;
        [Tooltip("Negative Y moves the planet DOWN in the viewport (out from under the top-left IMGUI).")]
        [SerializeField] Vector3 planetPosition = new(0f, -0.6f, 0f);
        [SerializeField] int planetSeed = 42;
        [Tooltip("Equirectangular albedo width (height = half). 1024 reads much crisper than 512.")]
        [SerializeField] int textureSize = 1024;
        [Header("Atmosphere")]
        [Tooltip("Soft glow rim around the planet — reads as an atmosphere in space.")]
        [SerializeField] bool atmosphere = true;
        [SerializeField] Color atmosphereColor = new(1f, 0.55f, 0.22f, 0.5f); // Neon Hologram orange rim
        [Tooltip("Halo size relative to the planet radius.")]
        [SerializeField, Range(1f, 1.6f)] float atmosphereScale = 1.22f;
        [SerializeField] Color oceanColor    = new(0.10f, 0.28f, 0.55f);
        [SerializeField] Color coastColor    = new(0.60f, 0.72f, 0.55f);
        [SerializeField] Color landColor     = new(0.28f, 0.48f, 0.22f);
        [SerializeField] Color mountainColor = new(0.42f, 0.36f, 0.28f);
        [SerializeField] Color iceColor      = new(0.92f, 0.94f, 0.98f);

        [Header("Reference pool (ReferencePool below, in Resources/PlanetRefs)")]
        [Tooltip("How many references from the pool blend into each planet.")]
        [SerializeField, Range(1, 4)] int referencesPerPlanet = 2;
        [Tooltip("Size of the blend regions (lower = bigger contiguous patches).")]
        [SerializeField, Range(0.5f, 8f)] float mixNoiseScale = 2.5f;
        [Tooltip("Overlay latitude ice caps on top of the blended surface.")]
        [SerializeField] bool addIceCaps = true;

        [Header("Per-planet uniqueness (HSV jitter driven by seed)")]
        [Tooltip("Hue shift range. 0.12 ≈ ±40° hue rotation between different players.")]
        [SerializeField, Range(0f, 0.5f)] float hueJitter = 0.12f;
        [Tooltip("Saturation multiplier range.")]
        [SerializeField, Range(0f, 0.5f)] float saturationJitter = 0.15f;
        [Tooltip("Brightness multiplier range.")]
        [SerializeField, Range(0f, 0.3f)] float valueJitter = 0.08f;

        [Header("Neon Hologram (2026-09-28)")]
        [Tooltip("Recolour the surface into the violet hologram palette the globe base is drawn on; the terrain detail stays.")]
        [SerializeField] bool hologramPalette = true;

        [Header("Seed source")]
        [Tooltip("When true, use GameContext.Seed (each player unique). When false, use planetSeed.")]
        [SerializeField] bool useGameSeed = true;

        [Header("Camera")]
        [SerializeField] Color skyColor = new(0.02f, 0.02f, 0.08f);
        [SerializeField] Vector3 cameraPosition = new(0f, 0f, -11f);
        [SerializeField] float fieldOfView = 45f;
        [Tooltip("Auto-rotate the planet at start? Nice observation demo, distracting during play.")]
        [SerializeField] bool autoSpinAtStart = false;

        [Header("Light")]
        [SerializeField] Vector3 lightRotation = new(50f, -30f, 0f);
        [SerializeField] float lightIntensity = 1.2f;

        static SceneBootstrap? s_instance;

        GameContext? _ctx;
        int _visualSeedCache;
        /// <summary>The generated albedo on the planet material (null until the first Update).</summary>
        Texture2D? _planetTex;

        void Awake()
        {
            s_instance = this;
            _ctx = GetComponent<GameContext>();
            SpawnCamera();
            SpawnLight();
            SpawnPlanet();
            SpawnStarfield();
            if (_ctx != null) _visualSeedCache = _ctx.VisualSeed;
        }

        // VisualSeed mixes in the home tile AND the resurfacing offset, so a
        // relocation (Blind Jump / Precision Warp) or a Planetary Resurfacing
        // item re-rolls the planet's look — "a new world".
        void Update()
        {
            // First paint happens here, not in Awake: a returning player's save is
            // adopted in LocalBootstrap.Start (which regenerates for the loaded
            // seed), so an Awake-time texture was a ~1M-pixel bake thrown away
            // before the first frame ever rendered.
            if (_planetTex == null) { RequestPlanetRefresh(); return; }
            if (_ctx?.State == null) return;
            if (_ctx.VisualSeed == _visualSeedCache) return;
            RequestPlanetRefresh(); // also re-syncs _visualSeedCache
        }

        /// <summary>
        /// Regenerate the planet's albedo from the CURRENT GameContext.VisualSeed —
        /// called after a save is adopted (cloud/local load, guest reseed) and on
        /// home relocation, since the Awake-time texture used the pre-load seed.
        /// </summary>
        public static void RequestPlanetRefresh()
        {
            var inst = s_instance;
            if (inst == null) return;
            if (inst._ctx?.State != null) inst._visualSeedCache = inst._ctx.VisualSeed;
            var mesh = GameObject.Find("Home Planet")?.transform.Find("Planet Mesh");
            var renderer = mesh != null ? mesh.GetComponent<Renderer>() : null;
            if (renderer == null || renderer.sharedMaterial == null) return;
            Texture2D tex;
            using (BootTrace.Step("planet")) tex = inst.PlanetTexture();
            renderer.sharedMaterial.mainTexture = tex;
            // Runtime textures aren't garbage-collected — every relocation /
            // resurfacing / new game used to leak the previous ~2.7 MB bake.
            if (inst._planetTex != null) Destroy(inst._planetTex);
            inst._planetTex = tex;
        }

        /// <summary>
        /// Deep-space backdrop behind the planet: two tiled star layers on big
        /// quads far past the planet (the base camera is fixed, so static quads
        /// read as an infinite sky). Reuses the map view's generated star texture.
        /// </summary>
        void SpawnStarfield()
        {
            if (GameObject.Find("Base Starfield") != null) return; // idempotent on domain reload
            var root = new GameObject("Base Starfield");

            void Layer(string name, float z, float alpha, float tiling)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = name;
                Destroy(quad.GetComponent<Collider>());
                quad.transform.SetParent(root.transform, worldPositionStays: false);
                quad.transform.localPosition = new Vector3(0f, 0f, z);
                // Cover the camera frustum at depth z with generous margin (any aspect).
                float dist = z - cameraPosition.z;
                float height = 2f * dist * Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.6f;
                quad.transform.localScale = new Vector3(height, height, 1f);
                var mat = MapVisuals.UnlitTransparent(MapVisuals.Stars);
                mat.color = new Color(1f, 1f, 1f, alpha);
                mat.mainTextureScale = new Vector2(tiling, tiling);
                quad.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
            Layer("Stars Far", 60f, 0.55f, 7f);
            Layer("Stars Near", 45f, 0.85f, 4f);
        }

        void SpawnCamera()
        {
            if (Camera.main != null) return; // idempotent: don't stack cameras on domain reload
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            go.transform.position = cameraPosition;
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = fieldOfView;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = skyColor;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            // AudioListener so Unity doesn't warn about zero AudioListeners.
            go.AddComponent<AudioListener>();
        }

        void SpawnLight()
        {
            if (GameObject.Find("Directional Light") != null) return;
            var go = new GameObject("Directional Light");
            go.transform.rotation = Quaternion.Euler(lightRotation);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = lightIntensity;
            light.shadows = LightShadows.Soft;
        }

        void SpawnPlanet()
        {
            if (GameObject.Find("Home Planet") != null) return;

            // Root: empty, unscaled. Rotation lives here (auto-spin + drag). Building markers
            // parent to this so they don't inherit the mesh's scale factor.
            var root = new GameObject("Home Planet");
            root.transform.position = planetPosition;
            var spin = root.AddComponent<PlanetSpin>();
            spin.enabled = autoSpinAtStart;

            // Visual mesh: child sphere with the actual radius scaling and texture material.
            var mesh = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            mesh.name = "Planet Mesh";
            mesh.transform.SetParent(root.transform, worldPositionStays: false);
            mesh.transform.localPosition = Vector3.zero;
            mesh.transform.localScale = Vector3.one * planetRadius * 2f; // default sphere has radius 0.5
            // Remove the sphere's collider — we don't want raycasts hitting the planet;
            // only the building markers are interactive.
            var meshCollider = mesh.GetComponent<Collider>();
            if (meshCollider != null) Destroy(meshCollider);

            var mat = MapVisuals.LitOpaque();
            if (mat != null)
            {
                // Albedo arrives on the first Update (see there) — once, for the
                // seed that is actually in play.
                mat.SetFloat("_Smoothness", 0.2f);
                mesh.GetComponent<Renderer>().sharedMaterial = mat;
            }
            else
            {
                Debug.LogWarning("[GalaxyRoyale] URP Lit shader not found — using default material. Is URP configured?");
            }

            // Atmosphere halo — an OUTER GLOW ringing the whole planet. Two soft
            // glow layers on a NON-ROTATING holder (a sibling of the spinning
            // planet root), centered on the planet and sitting just behind its far
            // face so the opaque sphere occludes the middle and only a symmetric
            // rim of light shows all the way around. (Bug fixed 2026-07-07: the
            // halo used to parent to the spinning root, so drag-rotating tilted
            // the flat billboard toward the camera — it read as a blue flat plate
            // low on the planet. Off the root, it never tilts.)
            if (atmosphere)
            {
                var atmoHolder = new GameObject("Planet Atmosphere");
                atmoHolder.transform.position = planetPosition;

                // Inner rim: tight, brighter — hugs the planet's edge.
                float innerSize = planetRadius * 2f * atmosphereScale;
                MapVisuals.Spawn(atmoHolder.transform, "Atmosphere Rim", MapVisuals.Glow,
                    new Vector3(0f, 0f, planetRadius * 1.02f), // just behind the far face
                    innerSize, innerSize, atmosphereColor, sortingOrder: -5);

                // Outer bloom: larger, fainter — the soft glow bleeding into space.
                float outerSize = planetRadius * 2f * (atmosphereScale + 0.5f);
                var bloomColor = new Color(atmosphereColor.r, atmosphereColor.g, atmosphereColor.b,
                    atmosphereColor.a * 0.45f);
                MapVisuals.Spawn(atmoHolder.transform, "Atmosphere Bloom", MapVisuals.Glow,
                    new Vector3(0f, 0f, planetRadius * 1.05f),
                    outerSize, outerSize, bloomColor, sortingOrder: -6);
            }
        }

        // ---- seamless equirectangular noise (kills the longitude fold) ----
        // The old generator sampled Perlin(u, v) directly, so u=0 and u=1 landed
        // on different noise values → a visible stitch down one side of the globe.
        // These helpers are PERIODIC in u (u=0 and u=1 return the same value) by
        // cross-blending the noise field with its one-period-shifted copy, so the
        // wrap is seamless while v still varies freely.

        static float TileNoise(float u, float v, float freq, float offX, float offY)
        {
            float a = Mathf.PerlinNoise(u * freq + offX, v * freq + offY);
            float b = Mathf.PerlinNoise((u - 1f) * freq + offX, v * freq + offY);
            return Mathf.Lerp(a, b, u); // at u=0 and u=1 both resolve to Perlin(offX,·)
        }

        static float TileFbm(float u, float v, float baseFreq, float offX, float offY, int octaves)
        {
            float n = 0f, amp = 1f, freq = baseFreq, total = 0f;
            for (int o = 0; o < octaves; o++)
            {
                n += amp * TileNoise(u, v, freq, offX, offY);
                total += amp;
                amp *= 0.5f;
                freq *= 2f;
            }
            return n / total;
        }

        /// <summary>
        /// Generate the planet's albedo texture. Same seed → same planet, always.
        ///
        /// If a reference pool is provided: pick N references (seed-driven), blend them
        /// per pixel via SEAMLESS weighted noise, then apply per-planet HSV jitter so two
        /// players with the same references still get visually distinct planets.
        ///
        /// If the pool is empty: richer procedural biomes (deep ocean → shelf → beach →
        /// grass → forest → rock → snow) with domain warping for organic coastlines and
        /// elevation shading for relief. All noise wraps in longitude — no stitch.
        /// </summary>
        Texture2D GeneratePlanetTexture(int seed, out byte[] bytes)
        {
            int width = textureSize;
            int height = textureSize / 2; // 2:1 equirectangular
            var rng = new System.Random(seed);

            // 1. Pick N distinct references from the pool, and read their pixels.
            var picks = new System.Collections.Generic.List<Reference>();
            {
                var pool = new System.Collections.Generic.List<string>(ReferencePool);
                int k = Mathf.Min(referencesPerPlanet, pool.Count);
                for (int i = 0; i < k; i++)
                {
                    int idx = rng.Next(pool.Count);
                    var tex = Resources.Load<Texture2D>("PlanetRefs/" + pool[idx]);
                    pool.RemoveAt(idx);
                    if (tex == null) continue;
                    picks.Add(new Reference(tex));
                    Resources.UnloadAsset(tex); // its pixels are copied; free the texture
                }
            }

            // 2. Deterministic HSV jitter for this planet.
            float hueShift = ((float)rng.NextDouble() * 2f - 1f) * hueJitter;
            float satMul   = 1f + ((float)rng.NextDouble() * 2f - 1f) * saturationJitter;
            float valMul   = 1f + ((float)rng.NextDouble() * 2f - 1f) * valueJitter;

            // 3. Independent noise offsets per picked reference (for weighted blending).
            var noiseOffsets = new Vector2[picks.Count];
            for (int i = 0; i < picks.Count; i++)
                noiseOffsets[i] = new Vector2((float)rng.NextDouble() * 1000f, (float)rng.NextDouble() * 1000f);

            // Fallback noise offsets (used only if the reference pool is empty).
            float fbOffX = (float)rng.NextDouble() * 1000f;
            float fbOffY = (float)rng.NextDouble() * 1000f;
            float warpOffX = (float)rng.NextDouble() * 1000f;
            float warpOffY = (float)rng.NextDouble() * 1000f;

            // Derived procedural palette (respects the inspector colors, adds the
            // in-between bands a richer planet needs).
            Color deepOcean = Color.Lerp(oceanColor, Color.black, 0.35f);
            Color forest    = Color.Lerp(landColor, Color.black, 0.28f);
            Color rock      = mountainColor;

            bytes = new byte[width * height * 4]; // RGBA32, bottom row first
            var weights = new float[picks.Count]; // reused per pixel (was 0.5M tiny allocations)

            for (int y = 0; y < height; y++)
            {
                float v = y / (float)(height - 1);
                float polar = Mathf.Abs(v - 0.5f) * 2f;
                for (int x = 0; x < width; x++)
                {
                    float u = x / (float)(width - 1);
                    Color c;
                    float elevation = 0.5f; // 0..1, drives elevation shading below

                    if (picks.Count > 0)
                    {
                        // Weighted blend of N reference samples — SEAMLESS weights.
                        float totalW = 0f;
                        for (int i = 0; i < picks.Count; i++)
                        {
                            weights[i] = TileNoise(u, v, mixNoiseScale, noiseOffsets[i].x, noiseOffsets[i].y);
                            totalW += weights[i];
                        }
                        c = new Color(0f, 0f, 0f, 0f);
                        if (totalW > 0f)
                            for (int i = 0; i < picks.Count; i++)
                                c += picks[i].Bilinear(u, v) * (weights[i] / totalW);
                        c.a = 1f;
                    }
                    else
                    {
                        // Domain warp (added to the noise OFFSET so it stays periodic
                        // in u) breaks up straight noise banding into organic coasts.
                        float warpX = (TileNoise(u, v, 3.5f, warpOffX, warpOffY) - 0.5f) * 0.7f;
                        float warpY = (TileNoise(u, v, 3.5f, warpOffX + 40f, warpOffY + 40f) - 0.5f) * 0.7f;
                        float h = TileFbm(u, v, 2.6f, fbOffX + warpX, fbOffY + warpY, 6);
                        h -= polar * 0.08f; // a touch more ocean/ice toward the poles
                        elevation = Mathf.Clamp01(h);

                        // Smoothly blended biome bands (no hard color steps).
                        if (h < 0.44f)       c = Color.Lerp(deepOcean, oceanColor, Mathf.InverseLerp(0.30f, 0.44f, h));
                        else if (h < 0.50f)  c = Color.Lerp(oceanColor, coastColor, Mathf.InverseLerp(0.44f, 0.50f, h));
                        else if (h < 0.56f)  c = Color.Lerp(coastColor, landColor, Mathf.InverseLerp(0.50f, 0.56f, h));
                        else if (h < 0.70f)  c = Color.Lerp(landColor, forest, Mathf.InverseLerp(0.56f, 0.70f, h));
                        else if (h < 0.82f)  c = Color.Lerp(forest, rock, Mathf.InverseLerp(0.70f, 0.82f, h));
                        else                 c = Color.Lerp(rock, iceColor, Mathf.InverseLerp(0.82f, 0.95f, h));

                        // Elevation shading — peaks catch light, valleys sit in shade.
                        // Water stays flat (it shouldn't look mountainous).
                        if (h >= 0.50f)
                            c *= 0.9f + 0.25f * Mathf.InverseLerp(0.50f, 1f, h);
                    }

                    // Per-planet HSV jitter — the "each player looks unique" step.
                    Color.RGBToHSV(c, out float hh, out float s, out float vv);
                    hh = (hh + hueShift + 1f) % 1f;
                    s = Mathf.Clamp01(s * satMul);
                    vv = Mathf.Clamp01(vv * valMul);
                    c = Color.HSVToRGB(hh, s, vv);
                    c.a = 1f;

                    // Polar ice caps, softened (only over land-ish elevation for the
                    // procedural path so oceans freeze as thin sheets, not blocks).
                    if (addIceCaps && polar > 0.74f)
                    {
                        float t = Mathf.InverseLerp(0.74f, 0.95f, polar);
                        if (picks.Count == 0) t *= Mathf.Lerp(0.6f, 1f, elevation);
                        c = Color.Lerp(c, iceColor, Mathf.Clamp01(t));
                    }
                    if (hologramPalette) c = Hologram(c);
                    int o = (y * width + x) * 4;
                    bytes[o] = ToByte(c.r);
                    bytes[o + 1] = ToByte(c.g);
                    bytes[o + 2] = ToByte(c.b);
                    bytes[o + 3] = 255;
                }
            }
            var result = NewAlbedo(width, height, bytes);

            var names = new System.Collections.Generic.List<string>();
            foreach (var p in picks) names.Add(p.Name);
            string mix = names.Count > 0 ? string.Join(" + ", names) : "(procedural)";
            Debug.Log($"[GalaxyRoyale] Planet seed={seed}: mix=[{mix}], hue+={hueShift:F2}, sat×={satMul:F2}, val×={valMul:F2}");
            return result;
        }

        static byte ToByte(float f) => (byte)(Mathf.Clamp01(f) * 255f + 0.5f);

        static Texture2D NewAlbedo(int width, int height, byte[] rgba)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: true);
            tex.SetPixelData(rgba, 0);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            tex.anisoLevel = 4; // crisper at the grazing angle near the limb
            tex.Apply(updateMipmaps: true, makeNoLongerReadable: true); // nothing reads it back
            return tex;
        }

        // ---- the reference pool and the bake cache (2026-09-29) ----
        // A cold start spent 18 s here in the Simulator: the bake sampled two
        // compressed 2K references through GetPixelBilinear (one engine call and a
        // block decode per sample, a million times), and the scene loaded all 13
        // references to use two. Now the two are loaded when a planet is baked,
        // read once, and sampled in managed code; and each baked look is cached,
        // so later boots just load its pixels.

        /// <summary>The pool, in the order seeds pick from (the scene's old list),
        /// as textures in Resources/PlanetRefs.</summary>
        static readonly string[] ReferencePool =
        {
            "2k_ceres_fictional", "2k_earth_daymap", "2k_earth_nightmap", "2k_mars", "mercury", "saturn",
            "fordite-20260705-014751", "fordite-20260705-014813", "marble-20260705-015006",
            "neon-lights-20260705-014824", "neon-lights-20260705-014833", "planet-20260705-014910",
            "zebra-lines-20260705-014924",
        };

        /// <summary>A reference's pixels, read once, with GetPixelBilinear's sampling.</summary>
        sealed class Reference
        {
            public readonly string Name;
            readonly Color32[] _px;
            readonly int _w, _h;
            readonly bool _repeatU, _repeatV;

            public Reference(Texture2D tex)
            {
                Name = tex.name;
                _px = tex.GetPixels32();
                _w = tex.width;
                _h = tex.height;
                _repeatU = tex.wrapModeU == TextureWrapMode.Repeat;
                _repeatV = tex.wrapModeV == TextureWrapMode.Repeat;
            }

            public Color Bilinear(float u, float v)
            {
                float x = u * _w - 0.5f, y = v * _h - 0.5f;
                int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
                float fx = x - x0, fy = y - y0;
                int x1 = Wrap(x0 + 1, _w, _repeatU), y1 = Wrap(y0 + 1, _h, _repeatV);
                x0 = Wrap(x0, _w, _repeatU);
                y0 = Wrap(y0, _h, _repeatV);
                Color32 a = _px[y0 * _w + x0], b = _px[y0 * _w + x1];
                Color32 c = _px[y1 * _w + x0], d = _px[y1 * _w + x1];
                return new Color(
                    Mix(a.r, b.r, c.r, d.r, fx, fy),
                    Mix(a.g, b.g, c.g, d.g, fx, fy),
                    Mix(a.b, b.b, c.b, d.b, fx, fy),
                    Mix(a.a, b.a, c.a, d.a, fx, fy));
            }

            static float Mix(byte a, byte b, byte c, byte d, float fx, float fy)
            {
                float bottom = a + (b - a) * fx, top = c + (d - c) * fx;
                return (bottom + (top - bottom) * fy) / 255f;
            }

            static int Wrap(int i, int n, bool repeat) =>
                repeat ? ((i % n) + n) % n : Mathf.Clamp(i, 0, n - 1);
        }

        /// <summary>Bump when the bake's output changes, to retire cached looks.</summary>
        const int BakeVersion = 2;

        /// <summary>The planet's albedo for the seed in play: from the cache when
        /// this look was baked before, otherwise baked now and cached.</summary>
        Texture2D PlanetTexture()
        {
            int seed = useGameSeed ? GetComponent<GameContext>().VisualSeed : planetSeed;
            int width = textureSize, height = textureSize / 2;
            string path = BakePath(seed);
            try
            {
                if (File.Exists(path))
                {
                    byte[] cached = File.ReadAllBytes(path);
                    if (cached.Length == width * height * 4) return NewAlbedo(width, height, cached);
                }
            }
            catch (Exception e) { Debug.LogWarning($"[GalaxyRoyale] Cached planet unreadable: {e.Message}"); }

            var tex = GeneratePlanetTexture(seed, out var bytes);
            try
            {
                // One look is on screen at a time; a new seed retires the old file.
                foreach (var old in Directory.GetFiles(Application.temporaryCachePath, "planet-*.rgba"))
                    if (old != path) File.Delete(old);
                File.WriteAllBytes(path, bytes);
            }
            catch (Exception e) { Debug.LogWarning($"[GalaxyRoyale] Couldn't cache the planet: {e.Message}"); }
            return tex;
        }

        /// <summary>Library/Caches/planet-{hash}.rgba, keyed on everything that shapes the bake.</summary>
        string BakePath(int seed)
        {
            string key = $"{BakeVersion}|{seed}|{textureSize}|{referencesPerPlanet}|{mixNoiseScale}|{addIceCaps}|" +
                         $"{hueJitter}|{saturationJitter}|{valueJitter}|{hologramPalette}|" +
                         $"{oceanColor}|{coastColor}|{landColor}|{mountainColor}|{iceColor}";
            uint hash = 2166136261; // FNV-1a: stable across runs, unlike string.GetHashCode
            foreach (char ch in key) { hash ^= ch; hash *= 16777619; }
            return Path.Combine(Application.temporaryCachePath, $"planet-{hash:x8}.rgba");
        }

        // Dark space-violet to orchid by brightness, with a trace of the planet's own
        // colour so every world still looks a little different.
        static readonly Color HoloDeep = new(0.03f, 0.016f, 0.07f);
        static readonly Color HoloMid = new(0.17f, 0.085f, 0.3f);
        static readonly Color HoloHigh = new(0.4f, 0.2f, 0.52f);
        static readonly Color HoloPeak = new(0.66f, 0.46f, 0.74f);

        static Color Hologram(Color c)
        {
            float l = Mathf.Clamp01(0.3f * c.r + 0.59f * c.g + 0.11f * c.b);
            var holo = l < 0.35f ? Color.Lerp(HoloDeep, HoloMid, l / 0.35f)
                : l < 0.7f ? Color.Lerp(HoloMid, HoloHigh, (l - 0.35f) / 0.35f)
                : Color.Lerp(HoloHigh, HoloPeak, (l - 0.7f) / 0.3f);
            var mixed = Color.Lerp(holo, c * 0.45f, 0.12f);
            mixed.a = 1f;
            return mixed;
        }
    }
}
