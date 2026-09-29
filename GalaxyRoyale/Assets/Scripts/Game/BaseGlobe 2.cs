// The globe base's camera rig (2026-09-28). The home planet turns under a
// fixed camera: yaw brings a longitude to the front, tilt looks down on it from
// above the equator, and zoom pulls back to orbit. Drag spins and tilts, pinch
// zooms; let go and the globe settles on the nearest district. The district
// tabs and the ORBIT / HOME / LAND button fly it there for you.
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
        [SerializeField] float districtTilt = 20f;
        [SerializeField] Vector3 districtCamera = new(0f, 1.25f, -9.3f);
        [Header("Orbit view")]
        [SerializeField] float orbitTilt = 60f;
        [SerializeField] Vector3 orbitCamera = new(0f, -0.35f, -17.5f);
        [Header("Feel")]
        [Tooltip("Degrees of spin for a drag across the whole screen.")]
        [SerializeField] float spinPerScreen = 120f;
        [SerializeField] float settleSeconds = 0.28f;
        [SerializeField] float minTiltOffset = -16f;
        [SerializeField] float maxTiltOffset = 34f;

        Transform? _planet;
        PlanetSpin? _spin;
        Camera? _cam;
        float _yaw, _yawTarget, _yawVel;
        float _tiltOffset, _tiltVel;
        float _zoom, _zoomTarget, _zoomVel;
        bool _dragging;

        /// <summary>0 = on a district, 1 = in orbit.</summary>
        public float Zoom => _zoom;
        public bool InOrbit => _zoomTarget > 0.5f;
        /// <summary>The longitude facing the camera.</summary>
        public float Yaw => _yaw;
        /// <summary>The district nearest the front — the one the base screen names.</summary>
        public BaseDistrict District => Nearest(_yaw);

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

        void LateUpdate()
        {
            if (!Acquire()) return;
            float dt = Time.unscaledDeltaTime;
            if (!_dragging)
            {
                _yaw = Mathf.SmoothDamp(_yaw, _yawTarget, ref _yawVel, settleSeconds, Mathf.Infinity, dt);
                if (!InOrbit) _tiltOffset = Mathf.SmoothDamp(_tiltOffset, 0f, ref _tiltVel, settleSeconds, Mathf.Infinity, dt);
            }
            _zoom = Mathf.SmoothDamp(_zoom, _zoomTarget, ref _zoomVel, settleSeconds * 1.2f, Mathf.Infinity, dt);
            Apply(snap: false);
        }

        void Apply(bool snap)
        {
            if (_planet == null) return;
            if (snap) { _yaw = _yawTarget; _zoom = _zoomTarget; }
            float tilt = Mathf.Lerp(districtTilt, orbitTilt, _zoom) + _tiltOffset;
            _planet.rotation = Quaternion.AngleAxis(-tilt, Vector3.right) * Quaternion.AngleAxis(_yaw, Vector3.up);
            if (_cam != null && _cam.isActiveAndEnabled)
                _cam.transform.position = Vector3.Lerp(districtCamera, orbitCamera, _zoom);
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
            _tiltOffset = Mathf.Clamp(_tiltOffset - deltaPx.y * k, minTiltOffset, maxTiltOffset);
        }

        /// <summary>The finger lifted: settle on the nearest district (not in orbit).</summary>
        public void Release()
        {
            if (!_dragging) return;
            _dragging = false;
            if (!InOrbit) _yawTarget = _yaw + Mathf.DeltaAngle(_yaw, Lon(Nearest(_yaw)));
        }

        /// <summary>Pinch or scroll: positive pulls in, negative pulls out to orbit.</summary>
        public void Pinch(float amount)
        {
            bool wasOrbit = InOrbit;
            _zoomTarget = Mathf.Clamp01(_zoomTarget - amount);
            // Crossing back from orbit lands on whichever district is in front.
            if (wasOrbit && !InOrbit) _yawTarget = _yaw + Mathf.DeltaAngle(_yaw, Lon(Nearest(_yaw)));
        }

        /// <summary>Pinch finished: snap the zoom to a district or to orbit.</summary>
        public void EndPinch()
        {
            _zoomTarget = _zoomTarget > 0.45f ? 1f : 0f;
            if (!InOrbit) _yawTarget = _yaw + Mathf.DeltaAngle(_yaw, Lon(Nearest(_yaw)));
        }

        // ---------- buttons ----------

        public void FlyTo(BaseDistrict district)
        {
            _zoomTarget = 0f;
            _yawTarget = _yaw + Mathf.DeltaAngle(_yaw, Lon(district));
            _tiltOffset = Mathf.Min(_tiltOffset, 0f);
        }

        public void Orbit()
        {
            _zoomTarget = 1f;
            _tiltOffset = 0f;
        }

        public void Land() => FlyTo(Nearest(_yaw));

        /// <summary>Jump straight to a view (launch hooks, first frame).</summary>
        public void Snap(BaseDistrict district, bool orbit = false)
        {
            _yawTarget = Lon(district);
            _zoomTarget = orbit ? 1f : 0f;
            _tiltOffset = 0f;
            Apply(snap: true);
        }

        static float Lon(BaseDistrict d) => (float)BaseLayout.DistrictLon(d);

        static BaseDistrict Nearest(float yaw)
        {
            var best = BaseDistrict.Command;
            float bestGap = float.MaxValue;
            foreach (BaseDistrict d in System.Enum.GetValues(typeof(BaseDistrict)))
            {
                float gap = Mathf.Abs(Mathf.DeltaAngle(yaw, Lon(d)));
                if (gap < bestGap) { bestGap = gap; best = d; }
            }
            return best;
        }
    }
}
