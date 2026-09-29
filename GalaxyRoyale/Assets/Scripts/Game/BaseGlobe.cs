// The globe base's camera rig (2026-09-28). The home planet turns under a
// fixed camera: yaw brings a longitude to the front, tilt looks down on it from
// above the equator, and zoom pulls back to orbit. Drag spins and tilts, pinch
// zooms; let go and the globe settles on the nearest district. The district
// tabs and the ORBIT / HOME / LAND button fly it there for you.
//
// The whole globe (2026-09-29): the northern band holds Command, the Mining
// Belt, the Frontier and, round the back, the Spaceport; the southern half is
// the Wilds. Pull the planet up past the equator and it tips south onto the
// Wilds, where it spins freely (no district to settle on); pull it back down
// and it returns to the nearest district of the band.
//
// Pads and buildings are parented to the planet (BuildingMarkers), so they turn
// with it. Longitudes follow Sim.BaseLayout: east is to the right.
using UnityEngine;
using GalaxyRoyale.Sim;

namespace GalaxyRoyale.Game
{
    public sealed class BaseGlobe : MonoBehaviour
    {
        public static BaseGlobe? Instance { get; private set; }

        [Header("District view")]
        [Tooltip("How far above the equator the camera looks down on a district.")]
        [SerializeField] float districtTilt = 25f;
        [Tooltip("12 units from the planet's centre puts neighbouring pads a third of the screen apart.")]
        [SerializeField] Vector3 districtCamera = new(0f, 0.34f, -12f);
        [Header("The Wilds (south)")]
        [Tooltip("The camera looks up at the southern hemisphere from this far below the equator.")]
        [SerializeField] float wildsTilt = -46f;
        [SerializeField] Vector3 wildsCamera = new(0f, -0.2f, -13.2f);
        [Tooltip("Let go with the planet tipped below this tilt and it settles on the Wilds.")]
        [SerializeField] float southThreshold = -6f;
        [Header("Orbit view")]
        [SerializeField] float orbitTilt = 62f;
        [SerializeField] Vector3 orbitCamera = new(0f, 0.1f, -22.4f);
        [Header("Feel")]
        [Tooltip("Degrees of spin for a drag across the whole screen.")]
        [SerializeField] float spinPerScreen = 120f;
        [SerializeField] float settleSeconds = 0.28f;

        Transform? _planet;
        PlanetSpin? _spin;
        Camera? _cam;
        float _yaw, _yawTarget, _yawVel;
        float _tilt, _tiltVel;
        float _zoom, _zoomTarget, _zoomVel;
        float _south, _southVel;
        bool _dragging;
        bool _wilds;

        /// <summary>0 = on a district, 1 = in orbit.</summary>
        public float Zoom => _zoom;
        public bool InOrbit => _zoomTarget > 0.5f;
        /// <summary>The longitude facing the camera.</summary>
        public float Yaw => _yaw;
        /// <summary>True while the view is (or is settling) on the southern hemisphere.</summary>
        public bool South => _wilds;
        /// <summary>The district in front — the one the base screen names.</summary>
        public BaseDistrict District => _wilds ? BaseDistrict.Wilds : Nearest(_yaw);

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        bool Acquire()
        {
            if (_planet != null) return true;
            var go = GameObject.Find("Home Planet");
            if (go == null) return false;
            _planet = go.transform;
            _spin = go.GetComponent<PlanetSpin>();
            // The base camera by name: Camera.main may be the galaxy map's while it's open.
            _cam = GameObject.Find("Main Camera")?.GetComponent<Camera>();
            if (_spin != null) _spin.enabled = false; // the districts are the view now, not a turntable
            Apply(snap: true);
            return true;
        }

        /// <summary>The tilt the view settles at: down on the band or up at the Wilds, near or from orbit.</summary>
        float TargetTilt() => _wilds ? (InOrbit ? -orbitTilt : wildsTilt) : (InOrbit ? orbitTilt : districtTilt);

        void LateUpdate()
        {
            if (!Acquire()) return;
            float dt = Time.unscaledDeltaTime;
            if (!_dragging)
            {
                _yaw = Mathf.SmoothDamp(_yaw, _yawTarget, ref _yawVel, settleSeconds, Mathf.Infinity, dt);
                _tilt = Mathf.SmoothDamp(_tilt, TargetTilt(), ref _tiltVel, settleSeconds * 1.2f, Mathf.Infinity, dt);
            }
            _zoom = Mathf.SmoothDamp(_zoom, _zoomTarget, ref _zoomVel, settleSeconds * 1.2f, Mathf.Infinity, dt);
            _south = Mathf.SmoothDamp(_south, _wilds ? 1f : 0f, ref _southVel, settleSeconds * 1.2f, Mathf.Infinity, dt);
            Apply(snap: false);
        }

        void Apply(bool snap)
        {
            if (_planet == null) return;
            if (snap)
            {
                _yaw = _yawTarget;
                _zoom = _zoomTarget;
                _tilt = TargetTilt();
                _south = _wilds ? 1f : 0f;
            }
            _planet.rotation = Quaternion.AngleAxis(-_tilt, Vector3.right) * Quaternion.AngleAxis(_yaw, Vector3.up);
            if (_cam != null && _cam.isActiveAndEnabled)
                _cam.transform.position = Vector3.Lerp(Vector3.Lerp(districtCamera, wildsCamera, _south), orbitCamera, _zoom);
        }

        // ---------- gestures (CameraController) ----------

        /// <summary>A one-finger drag, in screen pixels (y up).</summary>
        public void Drag(Vector2 deltaPx)
        {
            _dragging = true;
            float k = spinPerScreen / Mathf.Max(1f, Screen.width);
            _yaw -= deltaPx.x * k;               // drag left: the east comes round
            _yawTarget = _yaw;
            _yawVel = 0f;
            _tilt = Mathf.Clamp(_tilt - deltaPx.y * k, -80f, 80f);
            _tiltVel = 0f;
        }

        /// <summary>The finger lifted: tipped far enough south, settle on the Wilds;
        /// otherwise on the nearest district of the band (not in orbit).</summary>
        public void Release()
        {
            if (!_dragging) return;
            _dragging = false;
            _wilds = _tilt < (InOrbit ? 0f : southThreshold);
            if (!InOrbit && !_wilds) _yawTarget = _yaw + Mathf.DeltaAngle(_yaw, Lon(Nearest(_yaw)));
        }

        /// <summary>Pinch or scroll: positive pulls in, negative pulls out to orbit.</summary>
        public void Pinch(float amount)
        {
            bool wasOrbit = InOrbit;
            _zoomTarget = Mathf.Clamp01(_zoomTarget - amount);
            // Crossing back from orbit lands on whichever district is in front.
            if (wasOrbit && !InOrbit && !_wilds) _yawTarget = _yaw + Mathf.DeltaAngle(_yaw, Lon(Nearest(_yaw)));
        }

        /// <summary>Pinch finished: snap the zoom to a district or to orbit.</summary>
        public void EndPinch()
        {
            _zoomTarget = _zoomTarget > 0.45f ? 1f : 0f;
            if (!InOrbit && !_wilds) _yawTarget = _yaw + Mathf.DeltaAngle(_yaw, Lon(Nearest(_yaw)));
        }

        // ---------- buttons ----------

        public void FlyTo(BaseDistrict district)
        {
            _zoomTarget = 0f;
            _wilds = district == BaseDistrict.Wilds;
            if (!_wilds) _yawTarget = _yaw + Mathf.DeltaAngle(_yaw, Lon(district));
        }

        /// <summary>Over the Wilds, turn a longitude to the front (a sector, say).</summary>
        public void FlyToSouth(double lon)
        {
            _zoomTarget = 0f;
            _wilds = true;
            _yawTarget = _yaw + Mathf.DeltaAngle(_yaw, (float)lon);
        }

        public void Orbit() => _zoomTarget = 1f;

        public void Land()
        {
            if (_wilds) { _zoomTarget = 0f; return; }
            FlyTo(Nearest(_yaw));
        }

        /// <summary>Jump straight to a view (launch hooks, first frame).</summary>
        public void Snap(BaseDistrict district, bool orbit = false, double? lon = null)
        {
            _wilds = district == BaseDistrict.Wilds;
            _yawTarget = lon is double l ? (float)l : _wilds ? _yawTarget : Lon(district);
            _zoomTarget = orbit ? 1f : 0f;
            Apply(snap: true);
        }

        static float Lon(BaseDistrict d) => (float)BaseLayout.DistrictLon(d);

        /// <summary>The district of the northern band nearest a longitude.</summary>
        static BaseDistrict Nearest(float yaw)
        {
            var best = BaseDistrict.Command;
            float bestGap = float.MaxValue;
            foreach (var d in BaseLayout.NorthBand)
            {
                float gap = Mathf.Abs(Mathf.DeltaAngle(yaw, Lon(d)));
                if (gap < bestGap) { bestGap = gap; best = d; }
            }
            return best;
        }
    }
}
