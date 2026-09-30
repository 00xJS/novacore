// Life on the globe (2026-09-29): the base moves. A fleet sent out lifts off
// the Spaceport and climbs away on its engines; one coming home drops back
// onto the field. A few ships ride low orbits round the planet. A raid on the
// colony streaks in from space and flashes across the Command district. A
// finished upgrade goes up in a ring of light with its new level, and a
// charted sector of the Wilds lights up the same way.
//
// Everything here is a billboard on BuildingMarkers' overlay layer (drawn over
// the planet), so each one hides itself when the planet stands between it and
// the camera. Effects are made when they happen and dropped when they end.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Systems;
using GalaxyRoyale.Game.UI;
using Position = UnityEngine.UIElements.Position;

namespace GalaxyRoyale.Game
{
    [AddComponentMenu("GalaxyRoyale/Globe Fx")]
    [RequireComponent(typeof(GameContext))]
    public sealed class GlobeFx : MonoBehaviour
    {
        public static GlobeFx? Instance { get; private set; }

        const int MarkerLayer = 1;
        static readonly Color Orange = new(1f, 0.6f, 0.24f);
        static readonly Color Magenta = new(1f, 0.24f, 0.85f);
        static readonly Color Raid = new(1f, 0.3f, 0.36f);

        /// <summary>One billboard with a life: its step sets where it is and how it looks at k (0..1).</summary>
        sealed class Fx
        {
            public Transform Root = null!;
            public Transform Quad = null!;
            public Renderer Renderer = null!;
            public Material Material = null!;
            public float Born, Life;
            public Action<Fx, float> Step = null!;
            /// <summary>World-space direction the art's top should point along on screen (ships' noses, beams).</summary>
            public Vector3 Heading;
            public bool Persistent;
        }

        sealed class FloatLabel
        {
            public Label Label = null!;
            public Vector3 World;
            public float Born;
        }

        readonly List<Fx> _live = new();
        readonly List<FloatLabel> _labels = new();
        /// <summary>Marches seen last frame: their phase and their (live) ship counts.</summary>
        readonly Dictionary<int, (MarchPhase phase, Dictionary<HullId, int> ships)> _marches = new();
        readonly HashSet<int> _seen = new();
        readonly List<int> _gone = new();
        GameContext? _ctx;
        Transform? _planet;
        Camera? _cam;
        float _radius = 1f;
        VisualElement? _labelLayer;
        bool _marchesKnown;

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            _ctx = GetComponent<GameContext>();
            var planetGO = GameObject.Find("Home Planet");
            if (_ctx?.State == null || planetGO == null) return;
            _planet = planetGO.transform;
            var mesh = planetGO.transform.Find("Planet Mesh");
            _radius = mesh != null ? mesh.localScale.x * 0.5f : 1f;
            _cam = GameObject.Find("Main Camera")?.GetComponent<Camera>();
            _ctx.Events?.Subscribe(OnSimEvent);
            StartOrbitTraffic();
        }

        // ---------- building blocks ----------

        Fx Make(Texture tex, Color color, float width, float height, float life, Action<Fx, float> step, bool persistent = false)
        {
            var root = new GameObject("Globe Fx").transform;
            root.SetParent(_planet, false);
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(root, false);
            quad.transform.localScale = new Vector3(width, height, 1f);
            var mat = MapVisuals.UnlitTransparent(tex);
            mat.color = color;
            mat.renderQueue = 3100;
            var r = quad.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            root.gameObject.layer = MarkerLayer;
            quad.layer = MarkerLayer;
            var fx = new Fx
            {
                Root = root, Quad = quad.transform, Renderer = r, Material = mat, Born = Time.time, Life = life, Step = step,
                Persistent = persistent,
            };
            _live.Add(fx);
            step(fx, 0f);
            return fx;
        }

        /// <summary>A point on (or above) the planet in its local space.</summary>
        Vector3 Local(double lat, double lon, float lift) => BaseVisuals.Point(lat, lon, _radius * lift);

        /// <summary>True when the planet stands between the camera and a world point.</summary>
        bool Hidden(Vector3 world)
        {
            if (_cam == null || _planet == null) return false;
            var c = _cam.transform.position;
            var o = _planet.position;
            var d = world - c;
            float len = d.magnitude;
            if (len < 1e-4f) return false;
            d /= len;
            float t = Vector3.Dot(o - c, d);
            if (t < 0f || t > len) return false;
            return (o - c - d * t).sqrMagnitude < _radius * _radius * 0.985f;
        }

        void LateUpdate()
        {
            var state = _ctx?.State;
            if (state == null || _planet == null || _cam == null) return;
            WatchMarches(state);
            WatchMegaprojects(state);
            float now = Time.time;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var fx = _live[i];
                float k = fx.Persistent ? Mathf.Repeat((now - fx.Born) / fx.Life, 1f) : (now - fx.Born) / fx.Life;
                if (!fx.Persistent && k >= 1f)
                {
                    Destroy(fx.Material);
                    Destroy(fx.Root.gameObject);
                    _live.RemoveAt(i);
                    continue;
                }
                fx.Step(fx, k);
                // Billboard; the art's top along the heading as seen on screen.
                var pos = fx.Root.position;
                var toCam = (_cam.transform.position - pos).normalized;
                var up = fx.Heading == Vector3.zero ? _cam.transform.up : Vector3.ProjectOnPlane(fx.Heading, toCam);
                if (up.sqrMagnitude < 1e-6f) up = _cam.transform.up;
                fx.Root.rotation = Quaternion.LookRotation(-toCam, up.normalized);
                fx.Renderer.enabled = !Hidden(pos);
            }
            PlaceLabels(now);
        }

        // ---------- fleets: lift-off and landing at the Spaceport ----------

        void WatchMarches(GameState state)
        {
            _seen.Clear();
            foreach (var m in state.Marches)
            {
                _seen.Add(m.Id);
                if (!_marches.ContainsKey(m.Id) && _marchesKnown) LiftOff(m.Ships);
                // The march's own dictionary: when it's gone, it still holds who came home.
                _marches[m.Id] = (m.Phase, m.Ships);
            }
            if (_marches.Count > _seen.Count)
            {
                _gone.Clear();
                foreach (var kv in _marches) if (!_seen.Contains(kv.Key)) _gone.Add(kv.Key);
                foreach (int id in _gone)
                {
                    var (phase, ships) = _marches[id];
                    _marches.Remove(id);
                    if (phase == MarchPhase.Returning && CountShips(ships) > 0) Land(ships);
                }
            }
            _marchesKnown = true; // marches already out at boot don't lift off again
        }

        static int CountShips(Dictionary<HullId, int> ships)
        {
            int n = 0;
            foreach (var v in ships.Values) n += Math.Max(0, v);
            return n;
        }

        /// <summary>Up to five of the fleet's hulls, the biggest first.</summary>
        static List<HullId> Flight(Dictionary<HullId, int> ships)
        {
            var hulls = new List<HullId>();
            foreach (var kv in ships) if (kv.Value > 0) hulls.Add(kv.Key);
            hulls.Sort((a, b) => ShipArt.Bulk(b).CompareTo(ShipArt.Bulk(a)));
            var flight = new List<HullId>();
            int want = Math.Min(5, CountShips(ships));
            for (int i = 0; flight.Count < want && hulls.Count > 0; i++) flight.Add(hulls[i % hulls.Count]);
            return flight;
        }

        public void LiftOff(Dictionary<HullId, int> ships) => Flights(ships, up: true);

        public void Land(Dictionary<HullId, int> ships) => Flights(ships, up: false);

        void Flights(Dictionary<HullId, int> ships, bool up)
        {
            var planet = _planet;
            if (planet == null) return;
            var flight = Flight(ships);
            for (int i = 0; i < flight.Count; i++)
            {
                var tex = ShipArt.Photo(flight[i]);
                if (tex == null) continue;
                double lat = BaseLayout.PortLat + (i % 3 - 1) * 5.0;
                double lon = BaseLayout.PortLon + (i / 3 - 0.5) * 8.0 + (i % 2) * 4.0;
                var ground = Local(lat, lon, 1.01f);
                var normal = ground.normalized;
                // Climb away from the planet and north over the field (up the screen as you
                // look at the port), bending a little east as they gain height.
                var east = Vector3.Cross(Vector3.up, normal).normalized;
                var north = Vector3.ProjectOnPlane(Vector3.up, normal).normalized;
                var sky = ground + (normal * 0.9f + north * 0.95f + east * 0.25f) * _radius;
                float delay = i * 0.18f;
                float size = _radius * 0.085f * ShipArt.Bulk(flight[i]);
                var glowTex = Holo.GlowTexture;
                float life = 2.6f + delay;
                Make(tex, Color.white, size, size, life, (fx, k) =>
                {
                    float t = Mathf.Clamp01((k * life - delay) / 2.6f);
                    float u = up ? t * t : 1f - (1f - t) * (1f - t); // accelerate away, brake to land
                    var from = up ? ground : sky;
                    var to = up ? sky : ground;
                    fx.Root.localPosition = Vector3.Lerp(from, to, u);
                    fx.Heading = planet.TransformDirection(up ? (sky - ground) : (ground - sky));
                    float fade = up ? 1f - Mathf.SmoothStep(0.65f, 1f, t) : Mathf.SmoothStep(0f, 0.3f, t);
                    fx.Material.color = new Color(1f, 1f, 1f, t <= 0f && up ? 1f : fade);
                    fx.Quad.localScale = new Vector3(size, size, 1f) * (up ? 1f - 0.4f * t : 0.6f + 0.4f * t);
                });
                // The engine glow behind each ship.
                Make(glowTex, Orange, size * 1.1f, size * 2.2f, life, (fx, k) =>
                {
                    float t = Mathf.Clamp01((k * life - delay) / 2.6f);
                    float u = up ? t * t : 1f - (1f - t) * (1f - t);
                    var from = up ? ground : sky;
                    var to = up ? sky : ground;
                    var dir = (to - from).normalized;
                    fx.Root.localPosition = Vector3.Lerp(from, to, u) - dir * size * 0.55f;
                    fx.Heading = planet.TransformDirection(dir);
                    float fade = up ? 1f - Mathf.SmoothStep(0.6f, 1f, t) : Mathf.SmoothStep(0f, 0.3f, t) * (1f - Mathf.SmoothStep(0.85f, 1f, t));
                    fx.Material.color = new Color(Orange.r, Orange.g, Orange.b, fade);
                });
            }
            // Dust on the field where they lift off or touch down.
            Burst(Local(BaseLayout.PortLat, BaseLayout.PortLon, 1.01f), Orange, _radius * 0.35f, up ? 0.8f : 3.2f, up ? 0f : 2.2f);
        }

        // ---------- orbital traffic ----------

        void StartOrbitTraffic()
        {
            var planet = _planet;
            if (planet == null) return;
            var lanes = new (HullId hull, float tilt, float lift, float period, float phase)[]
            {
                (HullId.Hauler, 28f, 1.32f, 64f, 0.1f),
                (HullId.Fighter, -18f, 1.24f, 41f, 0.55f),
                (HullId.Probe, 64f, 1.42f, 88f, 0.8f),
            };
            foreach (var (hull, tilt, lift, period, phase) in lanes)
            {
                var tex = ShipArt.Photo(hull);
                if (tex == null) continue;
                float size = _radius * 0.06f * ShipArt.Bulk(hull);
                var axis = Quaternion.AngleAxis(tilt, Vector3.forward) * Vector3.up;
                var start = Vector3.Cross(axis, Vector3.forward).normalized;
                Make(tex, new Color(1f, 1f, 1f, 0.9f), size, size, period, (f, k) =>
                {
                    float a = (k + phase) * 360f;
                    var dir = Quaternion.AngleAxis(a, axis) * start;
                    f.Root.localPosition = dir * _radius * lift;
                    f.Heading = planet.TransformDirection(Vector3.Cross(axis, dir));
                }, persistent: true);
            }
        }

        // ---------- mega-projects in orbit (2026-09-30) ----------
        // Every finished stage shows: the Dyson Swarm's mirrors ring the planet in
        // gold, the Stargate hangs in high orbit (lit once it's whole), the Shield
        // Array's emitters glow cyan close in, and the Orbital Foundry's forges ride
        // a slow high lane.

        readonly List<Fx> _mega = new();
        string _megaKey = "";

        void WatchMegaprojects(GameState state)
        {
            string key = "";
            foreach (var def in Megaprojects.All) key += MegaprojectSystem.Stage(state, def.Kind);
            if (key == _megaKey) return;
            _megaKey = key;
            foreach (var fx in _mega)
            {
                _live.Remove(fx);
                Destroy(fx.Material);
                Destroy(fx.Root.gameObject);
            }
            _mega.Clear();
            if (_planet == null) return;

            // Dyson Swarm: six mirrors a stage on a tilted ring.
            int mirrors = MegaprojectSystem.Stage(state, MegaprojectKind.DysonSwarm) * 6;
            var swarmAxis = Quaternion.AngleAxis(12f, Vector3.forward) * Vector3.up;
            var swarmStart = Vector3.Cross(swarmAxis, Vector3.forward).normalized;
            for (int i = 0; i < mirrors; i++)
            {
                float phase = i / (float)mirrors;
                float size = _radius * 0.07f;
                _mega.Add(Make(Holo.GlowTexture, new Color(1f, 0.85f, 0.35f, 0.9f), size, size, 150f, (f, k) =>
                {
                    var dir = Quaternion.AngleAxis((k + phase) * 360f, swarmAxis) * swarmStart;
                    f.Root.localPosition = dir * _radius * 1.58f;
                }, persistent: true));
            }

            // Stargate: a ring in high orbit, bigger with each stage, lit inside when whole.
            int gate = MegaprojectSystem.Stage(state, MegaprojectKind.Stargate);
            if (gate > 0)
            {
                float size = _radius * (0.28f + 0.06f * gate);
                var spot = Local(38, -35, 1.9f);
                var ring = MapVisuals.Ring.texture;
                _mega.Add(Make(ring, new Color(0.35f, 0.9f, 1f, 0.95f), size, size, 30f, (f, k) =>
                {
                    f.Root.localPosition = spot;
                    f.Quad.localRotation = Quaternion.Euler(0f, 0f, k * 360f);
                }, persistent: true));
                if (gate >= Megaprojects.Stages)
                    _mega.Add(Make(Holo.GlowTexture, new Color(0.45f, 0.95f, 1f, 0.8f), size * 0.8f, size * 0.8f, 4f, (f, k) =>
                    {
                        f.Root.localPosition = spot;
                        f.Material.color = new Color(0.45f, 0.95f, 1f, 0.5f + 0.3f * Mathf.Sin(k * Mathf.PI * 2f));
                    }, persistent: true));
            }

            // Shield Array: two emitters a stage, pulsing close to the surface.
            int emitters = MegaprojectSystem.Stage(state, MegaprojectKind.ShieldArray) * 2;
            for (int i = 0; i < emitters; i++)
            {
                double lat = i % 2 == 0 ? 25 : -25;
                double lon = i * 360.0 / emitters;
                var at = Local(lat, lon, 1.14f);
                float size = _radius * 0.09f;
                float offset = i * 0.17f;
                _mega.Add(Make(Holo.GlowTexture, new Color(0.3f, 0.75f, 1f, 0.8f), size, size, 3f, (f, k) =>
                {
                    f.Root.localPosition = at;
                    f.Material.color = new Color(0.3f, 0.75f, 1f, 0.45f + 0.35f * Mathf.Sin((k + offset) * Mathf.PI * 2f));
                }, persistent: true));
            }

            // Orbital Foundry: a forge station for every other stage (one to start).
            int forges = (MegaprojectSystem.Stage(state, MegaprojectKind.OrbitalFoundry) + 1) / 2;
            var station = ShipArt.Photo(HullId.Atlas);
            var foundryAxis = Quaternion.AngleAxis(-40f, Vector3.forward) * Vector3.up;
            var foundryStart = Vector3.Cross(foundryAxis, Vector3.forward).normalized;
            for (int i = 0; i < forges && station != null; i++)
            {
                float phase = i / (float)Math.Max(1, forges);
                float size = _radius * 0.16f;
                _mega.Add(Make(station, new Color(1f, 0.8f, 0.6f, 1f), size, size, 220f, (f, k) =>
                {
                    var dir = Quaternion.AngleAxis((k + phase) * 360f, foundryAxis) * foundryStart;
                    f.Root.localPosition = dir * _radius * 1.75f;
                }, persistent: true));
            }
        }

        // ---------- bursts, beams and raids ----------

        /// <summary>A glow that swells and fades at a local point (optionally after a delay).</summary>
        void Burst(Vector3 local, Color color, float size, float life, float delay = 0f)
        {
            float total = life + delay;
            Make(Holo.GlowTexture, color, size, size, total, (fx, k) =>
            {
                float t = Mathf.Clamp01((k * total - delay) / life);
                fx.Root.localPosition = local;
                fx.Quad.localScale = new Vector3(size, size, 1f) * (0.3f + 0.9f * t);
                fx.Material.color = new Color(color.r, color.g, color.b, t <= 0f ? 0f : 0.95f * Mathf.Pow(1f - t, 1.3f));
            });
        }

        /// <summary>A finished upgrade: a ring of light, a beam, sparks and its new level.</summary>
        public void LevelUp(Vector3 local, int level)
        {
            var planet = _planet;
            if (planet == null) return;
            var normal = local.normalized;
            Burst(local, Orange, _radius * 0.8f, 1.4f);
            Burst(local, new Color(1f, 0.93f, 0.7f), _radius * 0.32f, 0.8f);
            var glow = Holo.GlowTexture;
            Make(glow, Orange, _radius * 0.07f, _radius * 0.7f, 1.4f, (fx, k) =>
            {
                fx.Root.localPosition = local + normal * _radius * (0.3f + 0.1f * k);
                fx.Heading = planet.TransformDirection(normal);
                fx.Material.color = new Color(1f, 0.75f, 0.45f, 0.8f * (1f - k));
            });
            var rnd = new System.Random(level * 7919 + (int)(local.x * 1000));
            var tangent = Vector3.Cross(normal, Vector3.up).normalized;
            if (tangent.sqrMagnitude < 0.5f) tangent = Vector3.right;
            var bitangent = Vector3.Cross(normal, tangent);
            for (int i = 0; i < 9; i++)
            {
                float ang = (float)(rnd.NextDouble() * Mathf.PI * 2);
                var spread = (tangent * Mathf.Cos(ang) + bitangent * Mathf.Sin(ang)) * (float)(0.15 + 0.2 * rnd.NextDouble());
                float speed = 0.35f + 0.35f * (float)rnd.NextDouble();
                float delay = (float)rnd.NextDouble() * 0.3f;
                Make(glow, new Color(1f, 0.85f, 0.5f), _radius * 0.035f, _radius * 0.035f, 1.3f + delay, (fx, k) =>
                {
                    float t = Mathf.Clamp01((k * (1.3f + delay) - delay) / 1.3f);
                    fx.Root.localPosition = local + (normal * speed + spread) * _radius * t;
                    fx.Material.color = new Color(1f, 0.85f, 0.5f, t <= 0f ? 0f : 1f - t);
                });
            }
            FloatText(planet.TransformPoint(local), $"LEVEL {level}", UiTheme.Energy);
        }

        /// <summary>A raid on the colony: streaks in from space onto the Command district, then flashes.</summary>
        public void RaidStrike()
        {
            var planet = _planet;
            if (planet == null) return;
            var rnd = new System.Random(Environment.TickCount);
            var glow = Holo.GlowTexture;
            for (int i = 0; i < 9; i++)
            {
                var target = Local(10 + rnd.NextDouble() * 38, -28 + rnd.NextDouble() * 56, 1.01f);
                var normal = target.normalized;
                var side = Vector3.Cross(normal, UnityEngine.Random.onUnitSphere).normalized;
                var from = target + (normal * 1.6f + side * 0.9f) * _radius;
                float delay = (float)rnd.NextDouble() * 0.9f;
                float life = 0.7f;
                float total = delay + life + 1f;
                foreach (var (width, color) in new[] { (0.06f, Raid), (0.014f, new Color(1f, 0.9f, 0.9f)) })
                    Make(glow, color, _radius * width, _radius * 0.5f, total, (fx, k) =>
                    {
                        float t = Mathf.Clamp01((k * total - delay) / life);
                        fx.Root.localPosition = Vector3.Lerp(from, target, t * t);
                        fx.Heading = planet.TransformDirection(target - from);
                        fx.Material.color = new Color(color.r, color.g, color.b, t <= 0f || t >= 1f ? 0f : 0.95f);
                    });
                Burst(target, new Color(1f, 0.55f, 0.3f), _radius * 0.3f, 0.9f, delay + life);
            }
        }

        // ---------- floating labels ----------

        void FloatText(Vector3 world, string text, Color color)
        {
            _labelLayer ??= UIController.Instance?.CreateFxLayer();
            if (_labelLayer == null) return;
            var label = Widgets.Heading(text, 18, color, 3f);
            label.pickingMode = PickingMode.Ignore;
            label.style.position = Position.Absolute;
            label.style.left = 0;
            label.style.top = 0;
            label.style.translate = new Translate(Length.Percent(-50), 0);
            // A dark edge under the glow so it reads over the building art.
            label.style.textShadow = new TextShadow { offset = new Vector2(1.5f, 1.5f), blurRadius = 3f, color = new Color(0.02f, 0.01f, 0.05f, 0.95f) };
            _labelLayer.Add(label);
            _labels.Add(new FloatLabel { Label = label, World = world, Born = Time.time });
        }

        void PlaceLabels(float now)
        {
            if (_labelLayer?.panel == null) return;
            for (int i = _labels.Count - 1; i >= 0; i--)
            {
                var l = _labels[i];
                float k = (now - l.Born) / 2.2f;
                var screen = _cam!.WorldToScreenPoint(l.World);
                if (k >= 1f || screen.z < 0f)
                {
                    l.Label.RemoveFromHierarchy();
                    _labels.RemoveAt(i);
                    continue;
                }
                var p = RuntimePanelUtils.ScreenToPanel(_labelLayer.panel, new Vector2(screen.x, Screen.height - screen.y));
                l.Label.style.left = p.x;
                // Above the building's art (it stands about 100 points tall on screen), rising.
                l.Label.style.top = p.y - 130f - 50f * k;
                l.Label.style.opacity = Hidden(l.World) ? 0f : Mathf.Clamp01(1.4f - 1.4f * k);
            }
        }

        // ---------- demo (GR_OPEN=fxdemo) ----------

        /// <summary>Every effect in turn for a minute, wherever the camera is looking.</summary>
        public System.Collections.IEnumerator Demo()
        {
            var fleet = new Dictionary<HullId, int> { [HullId.Vanguard] = 2, [HullId.Corsair] = 2, [HullId.Fighter] = 12 };
            var cc = BaseLayout.BuildingPad(BuildingId.CommandCenter);
            for (int i = 0; i < 12; i++)
            {
                var globe = BaseGlobe.Instance;
                switch (i % 3)
                {
                    case 0:
                        globe?.FlyTo(BaseDistrict.Command);
                        yield return new WaitForSeconds(1.2f);
                        LevelUp(Local(cc.Lat, cc.Lon, 1.01f), 11 + i);
                        break;
                    case 1:
                        RaidStrike();
                        break;
                    default:
                        globe?.FlyTo(BaseDistrict.Spaceport);
                        yield return new WaitForSeconds(1.2f);
                        LiftOff(fleet);
                        yield return new WaitForSeconds(2.6f);
                        Land(fleet);
                        break;
                }
                yield return new WaitForSeconds(3f);
            }
        }

        // ---------- sim events ----------

        void OnSimEvent(SimEvent e)
        {
            var state = _ctx?.State;
            if (state == null || _planet == null) return;
            switch (e)
            {
                case BuildingCompleted done:
                {
                    BasePad? pad = null;
                    if (done.MineId is int mineId)
                    {
                        var mine = BuildingSystem.GetMine(state, mineId);
                        if (mine != null) pad = BaseLayout.MinePad(mine.Type, BaseLayout.MineTier(state, mine));
                    }
                    else
                    {
                        foreach (var p in BaseLayout.Pads)
                            if (p.Kind == PadKind.Building && p.Building == done.Building) { pad = p; break; }
                    }
                    if (pad != null) LevelUp(Local(pad.Lat, pad.Lon, 1.01f), done.Level);
                    break;
                }
                case ColonyRaided:
                    RaidStrike();
                    break;
                case WildsSurveyed surveyed:
                {
                    var (lat, lon, _) = WildsLayout.Place(surveyed.Sector);
                    var at = Local(lat, lon, 1.01f);
                    Burst(at, Magenta, _radius * 0.5f, 1.3f);
                    Burst(at, new Color(1f, 0.85f, 0.95f), _radius * 0.2f, 0.7f);
                    break;
                }
            }
        }
    }
}
