// The globe base's camera rig (2026-09-28). The home planet turns under a
// fixed camera: yaw brings a longitude to the front, tilt looks down on it from
// above the equator, and zoom pulls back to orbit. Drag spins and tilts, pinch
// zooms. Free roam (user 2026-09-29): nothing snaps. Let go and the globe
// coasts to a stop wherever it is, a pinch stops at any height between the
// district view and orbit; only the district tabs and ORBIT / HOME / LAND fly
// the camera to a set view.
//
// The whole globe (2026-09-29): the northern band holds Command, the Mining
// Belt, the Frontier and, round the back, the Spaceport; the southern half is
// the Wilds. Pull the planet up past the equator and it tips south onto the
// Wilds (the camera eases round to look up at it); pull it back down and the
// band's districts are in front again.
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
        [Tooltip("Tipped below this tilt the view counts as the Wilds (the Wilds HUD shows).")]
        [SerializeField] float southThreshold = -6f;
        [Header("The Citadel (north pole)")]
        [Tooltip("Looking down on the pole: the Citadel's ring of pads.")]
        [SerializeField] float citadelTilt = 74f;
        [Tooltip("Tipped above this tilt (and not in orbit) the view counts as the Citadel.")]
        [SerializeField] float northThreshold = 66f;
        [Header("Orbit view")]
        [SerializeField] float orbitTilt = 62f;
        [SerializeField] Vector3 orbitCamera = new(0f, 0.1f, -22.4f);
        [Header("Feel")]
        [Tooltip("Degrees of spin for a drag across the whole screen.")]
        [SerializeField] float spinPerScreen = 120f;
        [SerializeField] float settleSeconds = 0.28f;
        [Tooltip("How quickly a flicked globe coasts to a stop (1/s).")]
        [SerializeField] float coastDamping = 5f;

        Transform? _planet;
        PlanetSpin? _spin;
        Camera? _cam;
        float _yaw, _yawTarget, _yawVel;
        float _tilt, _tiltTarget, _tiltVel;
        float _zoom, _zoomTarget, _zoomVel;
        float _south, _southVel;
        bool _dragging;
        bool _wilds;
        bool _citadel;
        /// <summary>A button is flying the camera to a set view; otherwise it stays put.</summary>
        bool _flying;
        /// <summary>Drag speed (deg/s) carried on after the finger lifts.</summary>
        Vector2 _coast;

        /// <summary>0 = on a district, 1 = in orbit.</summary>
        public float Zoom => _zoom;
        public bool InOrbit => _zoomTarget > 0.5f;
        /// <summary>How far the view is tipped north (+) or south (−), degrees.</summary>
        public float Tilt => _tilt;
        /// <summary>The longitude facing the camera.</summary>
        public float Yaw => _yaw;
        /// <summary>True while the view is (or is settling) on the southern hemisphere.</summary>
        public bool South => _wilds;
        /// <summary>The district in front — the one the base screen names.</summary>
        public BaseDistrict District => _wilds ? BaseDistrict.Wilds : _citadel ? BaseDistrict.Citadel : Nearest(_yaw);

        void Awake()
        {
            Instance = this;
            _tiltTarget = districtTilt; // first frame: looking down on the Command district
        }

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

        /// <summary>The tilt a button flies to: down on the band or up at the Wilds, near or from orbit.</summary>
        float ViewTilt(bool wilds, bool orbit) => wilds ? (orbit ? -orbitTilt : wildsTilt) : (orbit ? orbitTilt : districtTilt);

        void LateUpdate()
        {
            if (!Acquire()) return;
            float dt = Time.unscaledDeltaTime;
            if (_flying)
            {
                _yaw = Mathf.SmoothDamp(_yaw, _yawTarget, ref _yawVel, settleSeconds, Mathf.Infinity, dt);
                _tilt = Mathf.SmoothDamp(_tilt, _tiltTarget, ref _tiltVel, settleSeconds * 1.2f, Mathf.Infinity, dt);
                if (Mathf.Abs(Mathf.DeltaAngle(_yaw, _yawTarget)) < 0.05f && Mathf.Abs(_tilt - _tiltTarget) < 0.05f
                    && Mathf.Abs(_zoom - _zoomTarget) < 0.002f)
                    _flying = false;
            }
            else if (!_dragging && _coast.sqrMagnitude > 0.01f)
            {
                // Coast after a flick, then stay wherever the globe stops.
                _yaw += _coast.x * dt;
                _tilt = Mathf.Clamp(_tilt + _coast.y * dt, -TiltLimit, TiltLimit);
                _coast *= Mathf.Exp(-coastDamping * dt);
            }
            if (!_flying) { _yawTarget = _yaw; _tiltTarget = _tilt; }
            UpdateSouth();
            _zoom = Mathf.SmoothDamp(_zoom, _zoomTarget, ref _zoomVel, settleSeconds * 1.2f, Mathf.Infinity, dt);
            // The camera eases from the band's framing to the Wilds' as the planet tips south.
            float south = Mathf.Clamp01(Mathf.InverseLerp(0f, wildsTilt, _tilt));
            _south = Mathf.SmoothDamp(_south, south, ref _southVel, settleSeconds * 0.5f, Mathf.Infinity, dt);
            Apply(snap: false);
        }

        const float TiltLimit = 80f;

        /// <summary>Which half the view is on, with a little hysteresis so the HUD doesn't flicker.</summary>
        void UpdateSouth()
        {
            if (_flying) return; // FlyTo already chose
            if (!_wilds && _tilt < southThreshold - 2f) _wilds = true;
            else if (_wilds && _tilt > southThreshold + 2f) _wilds = false;
            // The Citadel on the pole (2026-09-30): tipped far enough north, close in.
            bool north = _zoomTarget < 0.5f && _tilt > northThreshold;
            if (!_citadel && north && _tilt > northThreshold + 2f) _citadel = true;
            else if (_citadel && (_zoomTarget >= 0.5f || _tilt < northThreshold - 2f)) _citadel = false;
        }

        void Apply(bool snap)
        {
            if (_planet == null) return;
            if (snap)
            {
                _yaw = _yawTarget;
                _zoom = _zoomTarget;
                _tilt = _tiltTarget;
                _south = Mathf.Clamp01(Mathf.InverseLerp(0f, wildsTilt, _tilt));
                _flying = false;
                _coast = Vector2.zero;
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
            _flying = false; // grabbing the globe cancels any fly-to
            float k = spinPerScreen / Mathf.Max(1f, Screen.width);
            float dYaw = -deltaPx.x * k;         // drag left: the east comes round
            float dTilt = -deltaPx.y * k;
            _yaw += dYaw;
            _tilt = Mathf.Clamp(_tilt + dTilt, -TiltLimit, TiltLimit);
            _yawVel = _tiltVel = 0f;
            // Remember the finger's speed so a flick keeps the globe turning a moment.
            float dt = Mathf.Max(1f / 120f, Time.unscaledDeltaTime);
            _coast = Vector2.Lerp(_coast, new Vector2(dYaw, dTilt) / dt, 0.5f);
        }

        /// <summary>The finger lifted: the globe coasts to a stop where it is, no snapping.</summary>
        public void Release()
        {
            if (!_dragging) return;
            _dragging = false;
            // A finger held still before lifting shouldn't fling the globe.
            if (_coast.magnitude < 20f) _coast = Vector2.zero;
            _coast = Vector2.ClampMagnitude(_coast, 400f);
        }

        /// <summary>Pinch or scroll: positive pulls in, negative pulls out to orbit.</summary>
        public void Pinch(float amount)
        {
            _zoomTarget = Mathf.Clamp01(_zoomTarget - amount);
        }

        /// <summary>Pinch finished: the zoom stays wherever the fingers left it.</summary>
        public void EndPinch() { }

        // ---------- buttons ----------

        /// <summary>Centre a district (the tabs): the one set view per district.</summary>
        public void FlyTo(BaseDistrict district)
        {
            _wilds = district == BaseDistrict.Wilds;
            _citadel = district == BaseDistrict.Citadel;
            // The Wilds and the Citadel keep the longitude you were at; the band's tabs face their district.
            float yaw = _wilds || _citadel ? _yaw : Lon(district);
            Fly(yaw, _citadel ? citadelTilt : ViewTilt(_wilds, false), 0f);
        }

        /// <summary>Over the Wilds, turn a longitude to the front (a sector, say).</summary>
        public void FlyToSouth(double lon)
        {
            _wilds = true;
            Fly((float)lon, ViewTilt(true, false), 0f);
        }

        /// <summary>Pull out to orbit over the half you're looking at.</summary>
        public void Orbit() => Fly(_yaw, ViewTilt(_wilds, true), 1f);

        /// <summary>From orbit, land on the district in front (or the Wilds below).</summary>
        public void Land()
        {
            if (_wilds) { Fly(_yaw, ViewTilt(true, false), 0f); return; }
            if (_tilt > northThreshold) { FlyTo(BaseDistrict.Citadel); return; }
            FlyTo(Nearest(_yaw));
        }

        void Fly(float yaw, float tilt, float zoom)
        {
            _yawTarget = _yaw + Mathf.DeltaAngle(_yaw, yaw);
            _tiltTarget = tilt;
            _zoomTarget = zoom;
            _coast = Vector2.zero;
            _flying = true;
        }

        /// <summary>Jump straight to a view (launch hooks, first frame).</summary>
        public void Snap(BaseDistrict district, bool orbit = false, double? lon = null)
        {
            _wilds = district == BaseDistrict.Wilds;
            _citadel = district == BaseDistrict.Citadel && !orbit;
            _yawTarget = lon is double l ? (float)l : _wilds || _citadel ? _yawTarget : Lon(district);
            _zoomTarget = orbit ? 1f : 0f;
            _tiltTarget = _citadel ? citadelTilt : ViewTilt(_wilds, orbit);
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
