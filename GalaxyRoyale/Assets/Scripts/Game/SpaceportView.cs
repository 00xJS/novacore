// The Spaceport (2026-09-29): the landing field on the far side of the
// northern band, where the docked fleet parks — the new ship art laid flat on
// the field in rows, nose to the north, the heaviest hulls at the back and one
// sprite for every few ships of a big fleet — with a chip counting them. A tap
// on the field opens the fleet.
//
// The field lies on the planet (the planet hides it from the other side); the
// ships ride BuildingMarkers' overlay layer like the building art, and hide
// when the Spaceport turns away. The chip shows while it faces the camera.
using System.Collections.Generic;
using UnityEngine;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Game.UI;

namespace GalaxyRoyale.Game
{
    [AddComponentMenu("GalaxyRoyale/Spaceport View")]
    [RequireComponent(typeof(GameContext))]
    public sealed class SpaceportView : MonoBehaviour
    {
        [Tooltip("Ship sprites on the field at most (a big fleet gets one per group of ships).")]
        [SerializeField] int maxSprites = 30;
        [SerializeField] float chipFacing = 0.5f;

        // The field is a hex with a corner to the north; ships park inside its lane
        // ring: a hex of this circumradius (degrees), flat sides this far east and west.
        const double Usable = 17.5;
        const double FieldHalfWidth = Usable * 0.866;
        const double RowsHalfHeight = 13.5;
        const int MarkerLayer = 1; // BuildingMarkers' overlay layer

        GameContext? _ctx;
        Transform? _planet;
        float _radius = 1f;
        Camera? _cam;
        GameObject? _field;
        Transform? _parked;
        BaseChipLayer? _chips;
        readonly Dictionary<HullId, Material> _materials = new();
        readonly List<Renderer> _ships = new();
        bool _shipsShown = true;
        string _key = "";
        float _nextRefresh;
        int _docked;

        void OnEnable() => CameraController.OnTapHit += HandleTapHit;
        void OnDisable() => CameraController.OnTapHit -= HandleTapHit;

        void Start()
        {
            _ctx = GetComponent<GameContext>();
            var planetGO = GameObject.Find("Home Planet");
            if (_ctx?.State == null || planetGO == null) return;
            _planet = planetGO.transform;
            var mesh = planetGO.transform.Find("Planet Mesh");
            _radius = mesh != null ? mesh.localScale.x * 0.5f : 1f;
            _cam = GameObject.Find("Main Camera")?.GetComponent<Camera>();
            BuildField();
            Refresh(_ctx.State);
        }

        /// <summary>A flat object on the planet at (lat, lon): its +y the outward normal, its +z north.</summary>
        GameObject Flat(string name, double lat, double lon, float lift)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_planet, false);
            var normal = BaseVisuals.Point(lat, lon, 1f);
            go.transform.localPosition = normal * _radius * lift;
            var north = Vector3.ProjectOnPlane(Vector3.up, normal).normalized;
            go.transform.localRotation = Quaternion.LookRotation(north, normal);
            return go;
        }

        void BuildField()
        {
            float size = _radius * (float)(BaseLayout.PortSpan * Mathf.Deg2Rad);
            _field = Flat("Spaceport Field", BaseLayout.PortLat, BaseLayout.PortLon, 1.0035f);
            _field.AddComponent<MeshFilter>().sharedMesh =
                BaseVisuals.PadMesh(size, UiTheme.A(UiTheme.Accent, 0.9f), new Color(0.05f, 0.025f, 0.12f, 0.55f), false, 0.035f);
            var mr = _field.AddComponent<MeshRenderer>();
            mr.sharedMaterial = BaseVisuals.SurfaceMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var box = _field.AddComponent<BoxCollider>();
            box.size = new Vector3(size * 1.75f, size * 0.12f, size * 1.9f);

            var inner = Flat("Spaceport Lanes", BaseLayout.PortLat, BaseLayout.PortLon, 1.0038f);
            inner.AddComponent<MeshFilter>().sharedMesh =
                BaseVisuals.PadMesh(size * 0.86f, UiTheme.A(UiTheme.Accent, 0.4f), Color.clear, true, 0.025f);
            var ir = inner.AddComponent<MeshRenderer>();
            ir.sharedMaterial = BaseVisuals.SurfaceMaterial();
            ir.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            _parked = new GameObject("Spaceport Ships").transform;
            _parked.SetParent(_planet, false);
        }

        Material? ShipMaterial(HullId hull)
        {
            if (_materials.TryGetValue(hull, out var mat)) return mat;
            var tex = ShipArt.Photo(hull);
            if (tex == null) return null;
            mat = MapVisuals.UnlitTransparent(tex);
            mat.color = Color.white;
            mat.renderQueue = 3010; // over the field (3005)
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f); // lying flat: never culled for the side it shows
            _materials[hull] = mat;
            return mat;
        }

        /// <summary>Sprites per hull: one a ship for a small fleet; ~maxSprites split by share for a big one.</summary>
        List<HullId> Allocate(GameState state)
        {
            var counts = new List<(HullId hull, int n)>();
            int total = 0;
            foreach (var hull in Ships.All)
                if (state.Ships.TryGetValue(hull, out var n) && n > 0) { counts.Add((hull, n)); total += n; }
            var sprites = new List<HullId>();
            foreach (var (hull, n) in counts)
            {
                int k = total <= maxSprites ? n : Mathf.Max(1, Mathf.RoundToInt(n * maxSprites / (float)total));
                for (int i = 0; i < k; i++) sprites.Add(hull);
            }
            while (sprites.Count > maxSprites + 4) sprites.RemoveAt(sprites.Count - 1);
            return sprites;
        }

        void Refresh(GameState state)
        {
            if (_parked == null) return;
            int docked = 0;
            var sb = new System.Text.StringBuilder();
            foreach (var hull in Ships.All)
                if (state.Ships.TryGetValue(hull, out var n) && n > 0) { docked += n; sb.Append(hull).Append(n).Append(','); }
            _docked = docked;
            string key = sb.ToString();
            if (key == _key) return;
            _key = key;
            for (int i = _parked.childCount - 1; i >= 0; i--) Destroy(_parked.GetChild(i).gameObject);
            _ships.Clear();

            var sprites = Allocate(state);
            if (sprites.Count == 0) return;
            // Heaviest at the back (north), small craft up front.
            sprites.Sort((a, b) => ShipArt.Bulk(b).CompareTo(ShipArt.Bulk(a)));

            // Rows at the biggest size that fits: a unit of `unit` degrees per bulk.
            const double gap = 0.5;
            List<List<HullId>> rows = new();
            double unit = 4.2;
            double RowHalfWidth(double dy) =>
                System.Math.Min(FieldHalfWidth, FieldHalfWidth * (Usable - System.Math.Abs(dy)) / (Usable * 0.5)) - 1.0;
            for (; unit > 0.8; unit -= 0.2)
            {
                rows = new List<List<HullId>>();
                var row = new List<HullId>();
                double used = 0, height = 0, y = RowsHalfHeight;
                bool fits = true;
                foreach (var hull in sprites)
                {
                    double s = unit * ShipArt.Bulk(hull);
                    double limit = 2 * RowHalfWidth(y - s * 0.5);
                    if (row.Count > 0 && used + gap + s > limit)
                    {
                        rows.Add(row);
                        y -= height + gap;
                        row = new List<HullId>();
                        used = 0;
                        height = 0;
                    }
                    used += (row.Count > 0 ? gap : 0) + s;
                    height = System.Math.Max(height, s);
                    row.Add(hull);
                }
                if (row.Count > 0) { rows.Add(row); y -= height; }
                if (y < -RowsHalfHeight) fits = false;
                if (fits) break;
            }

            // Place the rows, top (north) down, each centred.
            double top = RowsHalfHeight;
            foreach (var row in rows)
            {
                double h = 0, w = -gap;
                foreach (var hull in row) { h = System.Math.Max(h, unit * ShipArt.Bulk(hull)); w += unit * ShipArt.Bulk(hull) + gap; }
                double lat = BaseLayout.PortLat + top - h * 0.5;
                double cos = System.Math.Cos(lat * Mathf.Deg2Rad);
                double x = -w * 0.5;
                foreach (var hull in row)
                {
                    double s = unit * ShipArt.Bulk(hull);
                    double lon = BaseLayout.PortLon + (x + s * 0.5) / cos;
                    x += s + gap;
                    var mat = ShipMaterial(hull);
                    if (mat == null) continue;
                    var go = Flat($"Parked {hull}", lat, lon, 1.0045f);
                    go.transform.SetParent(_parked, true);
                    var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    Destroy(quad.GetComponent<Collider>());
                    quad.transform.SetParent(go.transform, false);
                    // Lie flat, face out, nose north: the quad faces -z, so point z into the planet.
                    quad.transform.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                    float size = _radius * (float)(s * Mathf.Deg2Rad);
                    quad.transform.localScale = new Vector3(size, size, 1f);
                    var r = quad.GetComponent<Renderer>();
                    r.sharedMaterial = mat;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    r.enabled = _shipsShown;
                    go.layer = MarkerLayer;
                    quad.layer = MarkerLayer;
                    _ships.Add(r);
                }
                top -= h + gap;
            }
        }

        void LateUpdate()
        {
            var state = _ctx?.State;
            if (state == null || _field == null || _cam == null || _planet == null) return;
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 1f;
                Refresh(state);
            }
            // The overlay layer draws over the planet: hide the ships once the port turns away.
            var toCamera = (_cam.transform.position - _planet.position).normalized;
            var outward = (_field.transform.position - _planet.position).normalized;
            bool shown = Vector3.Dot(outward, toCamera) > 0.18f;
            if (shown != _shipsShown)
            {
                _shipsShown = shown;
                foreach (var r in _ships) if (r != null) r.enabled = shown;
            }
            PlaceChip();
        }

        void PlaceChip()
        {
            _chips ??= UIController.Instance?.CreateBaseChipLayer();
            if (_chips == null) return;
            var globe = BaseGlobe.Instance;
            if (globe == null || globe.Zoom > 0.45f || !_cam!.isActiveAndEnabled || UIController.Instance?.HasModal == true)
            {
                _chips.Clear();
                return;
            }
            _chips.Begin();
            var centre = _planet!.position;
            var toCamera = (_cam.transform.position - centre).normalized;
            // Over the field's northern tip, clear of the district tabs below it.
            var anchor = _planet.TransformPoint(BaseVisuals.Point(BaseLayout.PortLat + BaseLayout.PortSpan + 3, BaseLayout.PortLon, _radius * 1.004f));
            float facing = Vector3.Dot((anchor - centre).normalized, toCamera);
            if (facing >= chipFacing && _chips.ToPanel(_cam.WorldToScreenPoint(anchor)) is { } p)
                _chips.Place("spaceport", BaseChipLayer.Kind.Online, p, null, "Spaceport",
                    _docked == 0 ? "NO SHIPS DOCKED" : $"{_docked:N0} DOCKED", alpha: Mathf.Clamp01((facing - chipFacing) / 0.15f));
            _chips.End();
        }

        void HandleTapHit(RaycastHit hit)
        {
            if (_field == null || hit.collider.gameObject != _field) return;
            var globe = BaseGlobe.Instance;
            if (globe != null && (globe.Zoom > 0.45f || globe.District != BaseDistrict.Spaceport))
            {
                globe.FlyTo(BaseDistrict.Spaceport);
                return;
            }
            GameAudio.Play(Sfx.Toggle);
            UIController.Instance?.SwitchView(ViewId.Fleet);
        }
    }
}
