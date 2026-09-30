// Pan/pinch-zoom/tap rig for the galaxy-map camera. Mirrors CameraController's
// gesture grammar: short press = tap, drag = pan, scroll/pinch = zoom.
//
// PERSPECTIVE TILT (2026-07-05, user request): the map is no longer a flat
// top-down orthographic chart. The camera is a PERSPECTIVE camera pitched off
// straight-down by `tiltDegrees`, framing a ground TARGET on the z=0 plane —
// so the galaxy recedes with real depth ("flying through space") instead of an
// x/y graph. Zoom is a dolly (change `ViewSize` → camera distance).
//
// To avoid rewriting MapView's ~30 zoom-scalar sites, the logical half-height
// `ViewSize` (in tiles, the old orthographicSize's meaning) is MIRRORED into
// the perspective camera's unused `orthographicSize` field every pose update —
// so all the existing `cam.orthographicSize` reads keep working unchanged.
//
// Pan and tap use ground-plane RAY intersection (correct for any camera pose),
// replacing the old orthographic ScreenToWorldPoint.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace GalaxyRoyale.Game
{
    [AddComponentMenu("GalaxyRoyale/Map Camera Controller")]
    public sealed class MapCameraController : MonoBehaviour
    {
        [Header("Perspective tilt (dial the feel on device)")]
        [Tooltip("Pitch off straight-down, degrees. 0 = flat top-down; 40–55 reads like classic mobile-strategy iso.")]
        [SerializeField] float tiltDegrees = 48f;
        [Tooltip("Perspective vertical field of view. Lower = flatter/more orthographic; higher = more depth falloff.")]
        [SerializeField] float fieldOfView = 32f;

        [Header("Zoom (view half-height, in tiles)")]
        [Tooltip("Closest zoom-in. Raised so you can't zoom in so close the map feels claustrophobic (user feedback).")]
        [SerializeField] float minViewSize = 26f;
        [Tooltip("Farthest zoom-out: the galaxy's top and bottom edges fill the map (user 2026-09-29; it was 2800, the whole galaxy small in the middle of the screen).")]
        [SerializeField] float maxViewSize = 1500f;
        [SerializeField] float mouseZoomFactor = 0.09f;
        [SerializeField] float pinchZoomFactor = 0.007f;

        [Header("Tap vs drag threshold")]
        [SerializeField] float tapPixelThreshold = 12f;
        [SerializeField] float tapMaxSeconds = 0.5f;

        /// <summary>Screen-GUI rects (y-down) where presses are ignored. MapView refreshes these each frame.</summary>
        public static readonly List<Rect> BlockRects = new();

        /// <summary>Set by MapView when it activates this rig.</summary>
        public Camera? Cam;
        /// <summary>World-space pan bounds (min/max corners of the padded sector).</summary>
        public Vector2 BoundsMin;
        public Vector2 BoundsMax;
        /// <summary>The galaxy's centre and half-extent (world units). Zoomed out, the
        /// framed point is held this far in from the edge less what the view shows, so
        /// the screen stays on the galaxy instead of empty space (user 2026-09-29).
        /// Radius 0 = no such limit.</summary>
        public Vector2 GalaxyCentre;
        public float GalaxyRadius;
        /// <summary>Tap handler — receives the world-space (z=0) point under the finger.</summary>
        public System.Action<Vector3>? OnTap;

        /// <summary>The ground point (z=0) the camera is framing. Pan/zoom/follow move this.</summary>
        public Vector2 Target { get; private set; }

        float _viewSize = 60f;
        /// <summary>Logical zoom half-height in tiles (mirrors the old orthographicSize).</summary>
        public float ViewSize
        {
            get => _viewSize;
            set { _viewSize = Mathf.Clamp(value, minViewSize, maxViewSize); Frame(Target); }
        }

        public float MinViewSize => minViewSize;
        public float MaxViewSize => maxViewSize;

        bool _pressing;
        Vector2 _pressStartPos;
        float _pressStartTime;
        bool _dragging;

        void OnEnable()  => EnhancedTouchSupport.Enable();
        void OnDisable() => EnhancedTouchSupport.Disable();

        // ---------- camera pose ----------

        Vector3 Forward => new(0f, Mathf.Sin(tiltDegrees * Mathf.Deg2Rad), Mathf.Cos(tiltDegrees * Mathf.Deg2Rad));
        Vector3 UpVec   => new(0f, Mathf.Cos(tiltDegrees * Mathf.Deg2Rad), -Mathf.Sin(tiltDegrees * Mathf.Deg2Rad));

        /// <summary>Frame a ground point (clamped to bounds) and rebuild the camera pose.</summary>
        public void Frame(Vector2 groundXY)
        {
            groundXY.x = Mathf.Clamp(groundXY.x, BoundsMin.x, BoundsMax.x);
            groundXY.y = Mathf.Clamp(groundXY.y, BoundsMin.y, BoundsMax.y);
            if (GalaxyRadius > 0f)
            {
                // What the view shows either side of the framed point: its width (aspect)
                // across, and a little less than its half-height up and down at the tilt.
                float aspect = Cam != null ? Cam.aspect : 0.46f;
                float reachX = Mathf.Max(0f, GalaxyRadius - _viewSize * aspect * 0.9f);
                float reachY = Mathf.Max(0f, GalaxyRadius - _viewSize * 0.8f);
                groundXY.x = Mathf.Clamp(groundXY.x, GalaxyCentre.x - reachX, GalaxyCentre.x + reachX);
                groundXY.y = Mathf.Clamp(groundXY.y, GalaxyCentre.y - reachY, GalaxyCentre.y + reachY);
            }
            Target = groundXY;
            ApplyPose();
        }

        public void ApplyPose()
        {
            if (Cam == null) return;
            Cam.orthographic = false;
            Cam.fieldOfView = fieldOfView;
            float d = _viewSize / Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
            var fwd = Forward;
            Cam.transform.position = new Vector3(Target.x, Target.y, 0f) - fwd * d;
            Cam.transform.rotation = Quaternion.LookRotation(fwd, UpVec);
            // Clip planes scale with the dolly distance (d ranges from tens to
            // thousands of tiles across the zoom range).
            Cam.nearClipPlane = Mathf.Max(0.3f, d * 0.02f);
            Cam.farClipPlane = d * 2f + 2000f;
            // Mirror the logical zoom into the unused ortho field so MapView's
            // scale/LOD code (which reads cam.orthographicSize) keeps working.
            Cam.orthographicSize = _viewSize;
        }

        /// <summary>Screen point → world point on the z=0 ground plane (ray intersection).</summary>
        public Vector3 GroundPoint(Vector2 screenPos)
        {
            if (Cam == null) return Vector3.zero;
            var ray = Cam.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f));
            float dz = ray.direction.z;
            if (Mathf.Abs(dz) < 1e-5f) return new Vector3(Target.x, Target.y, 0f);
            float t = -ray.origin.z / dz;
            var p = ray.origin + ray.direction * t;
            return new Vector3(p.x, p.y, 0f);
        }

        void Update()
        {
            if (Cam == null || !Cam.enabled) return;
            if (GUIUtility.hotControl != 0) { _pressing = false; _dragging = false; return; }

            HandleMouse();
            HandleTouch();
        }

        void HandleMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            if (mouse.leftButton.wasPressedThisFrame)
            {
                var pos = mouse.position.ReadValue();
                if (!Blocked(pos)) BeginPress(pos);
            }
            if (mouse.leftButton.wasReleasedThisFrame && _pressing)
                EndPress(mouse.position.ReadValue());
            if (_pressing && mouse.leftButton.isPressed)
            {
                var cur = mouse.position.ReadValue();
                MaybePromoteToDrag(cur);
                if (_dragging) Pan(cur, mouse.delta.ReadValue());
            }

            float scrollY = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scrollY) > 0.001f) Zoom(1f - Mathf.Sign(scrollY) * mouseZoomFactor);
        }

        void HandleTouch()
        {
            var active = Touch.activeTouches;
            if (active.Count == 1)
            {
                var t = active[0];
                if (t.phase == UnityEngine.InputSystem.TouchPhase.Began)
                {
                    if (!Blocked(t.screenPosition)) BeginPress(t.screenPosition);
                }
                else if (t.phase == UnityEngine.InputSystem.TouchPhase.Moved && _pressing)
                {
                    MaybePromoteToDrag(t.screenPosition);
                    if (_dragging) Pan(t.screenPosition, t.delta);
                }
                else if (t.phase == UnityEngine.InputSystem.TouchPhase.Ended && _pressing)
                {
                    EndPress(t.screenPosition);
                }
                else if (t.phase == UnityEngine.InputSystem.TouchPhase.Canceled)
                {
                    _pressing = false;
                    _dragging = false;
                }
            }
            else if (active.Count == 2)
            {
                _pressing = false;
                _dragging = false;

                var t0 = active[0];
                var t1 = active[1];
                Vector2 prev0 = t0.screenPosition - t0.delta;
                Vector2 prev1 = t1.screenPosition - t1.delta;
                float prevDist = Vector2.Distance(prev0, prev1);
                float curDist  = Vector2.Distance(t0.screenPosition, t1.screenPosition);
                if (prevDist > 1f) Zoom(1f - (curDist - prevDist) * pinchZoomFactor);
            }
        }

        static bool Blocked(Vector2 screenPos)
        {
            if (UI.UIController.IsPointerOverUI(screenPos)) return true;
            var guiPoint = new Vector2(screenPos.x, Screen.height - screenPos.y);
            foreach (var r in BlockRects)
                if (r.Contains(guiPoint)) return true;
            return false;
        }

        void BeginPress(Vector2 pos)
        {
            _pressing = true;
            _pressStartPos = pos;
            _pressStartTime = Time.time;
            _dragging = false;
        }

        void MaybePromoteToDrag(Vector2 pos)
        {
            if (_dragging || !_pressing) return;
            if (Vector2.Distance(pos, _pressStartPos) > tapPixelThreshold) _dragging = true;
        }

        void EndPress(Vector2 pos)
        {
            bool wasDragging = _dragging;
            float dist = Vector2.Distance(pos, _pressStartPos);
            float dur = Time.time - _pressStartTime;
            _pressing = false;
            _dragging = false;
            if (wasDragging) return;
            if (dist < tapPixelThreshold && dur < tapMaxSeconds && Cam != null)
                OnTap?.Invoke(GroundPoint(pos));
        }

        // Ground-locked pan: shift the target so the ground under the finger
        // stays under the finger (correct for any tilt/perspective).
        void Pan(Vector2 screenPos, Vector2 screenDelta)
        {
            if (Cam == null) return;
            Vector3 nowG  = GroundPoint(screenPos);
            Vector3 prevG = GroundPoint(screenPos - screenDelta);
            Frame(Target + new Vector2(prevG.x - nowG.x, prevG.y - nowG.y));
        }

        void Zoom(float factor)
        {
            ViewSize = _viewSize * factor; // clamps + re-poses
        }
    }
}
