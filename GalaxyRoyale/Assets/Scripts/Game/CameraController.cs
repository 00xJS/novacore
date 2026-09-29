// Planet-focused scene interaction, on Unity's new Input System.
//
//   Mouse (Editor):
//     • Left-click short (no drag)   = TAP → raycast + publish OnTapHit / OnTapEmpty
//     • Left-drag                    = spin and tilt the globe (BaseGlobe)
//     • Scroll wheel                 = zoom between a district and orbit
//   Touch (Device):
//     • 1-finger short press         = TAP
//     • 1-finger drag                = spin and tilt the globe
//     • 2-finger pinch               = zoom between a district and orbit
//
// Tap vs drag is decided by pixel distance moved and time held — press-and-hold
// beyond the threshold engages the drag; a quick release under the threshold is
// treated as a tap.
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace GalaxyRoyale.Game
{
    [AddComponentMenu("GalaxyRoyale/Camera Controller")]
    public sealed class CameraController : MonoBehaviour
    {
        [Header("Rotation (drag)")]
        [SerializeField] float mouseRotateSpeed = 0.3f;
        [SerializeField] float touchRotateSpeed = 0.2f;
        [Tooltip("Ignore drags/taps that start inside this rect. Should cover only interactive IMGUI elements.")]
        [SerializeField] Rect imguiBlockRect = new(10f, 170f, 240f, 265f);

        [Header("Zoom")]
        [SerializeField] float mouseZoomSpeed = 0.005f;
        [SerializeField] float pinchZoomSpeed = 0.015f;
        [SerializeField] float minCameraZ = -18f;
        [SerializeField] float maxCameraZ = -5f;

        [Header("Tap vs drag threshold")]
        [Tooltip("A press that moves less than this many pixels + releases within tapMaxSeconds is treated as a tap.")]
        [SerializeField] float tapPixelThreshold = 12f;
        [SerializeField] float tapMaxSeconds = 0.5f;

        /// <summary>Fired when a tap raycast hits a collider. Subscribe from BuildingMarkers etc.</summary>
        public static System.Action<RaycastHit>? OnTapHit;
        /// <summary>Fired when a tap lands on empty space (no collider hit). Use to deselect.</summary>
        public static System.Action? OnTapEmpty;
        /// <summary>Optional extra "don't drag / don't tap" rect (screen GUI coords). Overlay panels set this while they're open.</summary>
        public static Rect? ExtraBlockRect;

        Camera? _cam;
        Transform? _planet;
        PlanetSpin? _spin;

        // Press tracking (mouse + single-finger touch share this)
        bool _pressing;
        Vector2 _pressStartPos;
        float _pressStartTime;
        bool _dragging;
        bool _wasSpinningBeforeDrag;
        bool _pinching;

        void OnEnable()  => EnhancedTouchSupport.Enable();
        void OnDisable() => EnhancedTouchSupport.Disable();

        void Start() => AcquireRefs();

        void AcquireRefs()
        {
            _cam = Camera.main;
            var planetGO = GameObject.Find("Home Planet");
            if (planetGO != null)
            {
                _planet = planetGO.transform;
                _spin = planetGO.GetComponent<PlanetSpin>();
            }
        }

        void Update()
        {
            if (_cam == null || _planet == null) { AcquireRefs(); if (_cam == null) return; }
            if (GUIUtility.hotControl != 0) { EndDrag(); _pressing = false; return; }

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
                var guiPoint = new Vector2(pos.x, Screen.height - pos.y);
                if (!imguiBlockRect.Contains(guiPoint) && !ExtraContains(guiPoint)
                    && !UI.UIController.IsPointerOverUI(pos))
                {
                    BeginPress(pos);
                }
            }
            if (mouse.leftButton.wasReleasedThisFrame && _pressing)
            {
                EndPress(mouse.position.ReadValue());
            }
            if (_pressing && mouse.leftButton.isPressed)
            {
                var curPos = mouse.position.ReadValue();
                MaybePromoteToDrag(curPos);
                if (_dragging)
                {
                    Vector2 d = mouse.delta.ReadValue();
                    if (BaseGlobe.Instance != null) BaseGlobe.Instance.Drag(d);
                    else RotatePlanet(d.x * mouseRotateSpeed, d.y * mouseRotateSpeed);
                }
            }

            float scrollY = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scrollY) > 0.001f)
            {
                if (BaseGlobe.Instance != null) BaseGlobe.Instance.Pinch(scrollY / 600f);
                else Zoom(scrollY * mouseZoomSpeed);
            }
        }

        void HandleTouch()
        {
            var active = Touch.activeTouches;
            if (_pinching && active.Count < 2)
            {
                _pinching = false;
                BaseGlobe.Instance?.EndPinch();
            }
            if (active.Count == 1)
            {
                var t = active[0];
                if (t.phase == UnityEngine.InputSystem.TouchPhase.Began)
                {
                    var guiPoint = new Vector2(t.screenPosition.x, Screen.height - t.screenPosition.y);
                    if (!imguiBlockRect.Contains(guiPoint) && !ExtraContains(guiPoint)
                        && !UI.UIController.IsPointerOverUI(t.screenPosition)) BeginPress(t.screenPosition);
                }
                else if (t.phase == UnityEngine.InputSystem.TouchPhase.Moved && _pressing)
                {
                    MaybePromoteToDrag(t.screenPosition);
                    if (_dragging)
                    {
                        if (BaseGlobe.Instance != null) BaseGlobe.Instance.Drag(t.delta);
                        else RotatePlanet(t.delta.x * touchRotateSpeed, t.delta.y * touchRotateSpeed);
                    }
                }
                else if (t.phase == UnityEngine.InputSystem.TouchPhase.Ended && _pressing)
                {
                    EndPress(t.screenPosition);
                }
                else if (t.phase == UnityEngine.InputSystem.TouchPhase.Canceled)
                {
                    _pressing = false;
                    EndDrag();
                }
            }
            else if (active.Count == 2)
            {
                _pressing = false; // second finger cancels any 1-finger interaction
                EndDrag();

                var t0 = active[0];
                var t1 = active[1];
                Vector2 prev0 = t0.screenPosition - t0.delta;
                Vector2 prev1 = t1.screenPosition - t1.delta;
                float prevDist = Vector2.Distance(prev0, prev1);
                float curDist  = Vector2.Distance(t0.screenPosition, t1.screenPosition);
                if (BaseGlobe.Instance != null)
                {
                    _pinching = true;
                    // A pinch across the width of the screen goes all the way to orbit and back.
                    BaseGlobe.Instance.Pinch((curDist - prevDist) / Mathf.Max(1f, Screen.width) * 1.8f);
                }
                else Zoom((curDist - prevDist) * pinchZoomSpeed);
            }
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
            if (Vector2.Distance(pos, _pressStartPos) > tapPixelThreshold) BeginDrag();
        }

        void EndPress(Vector2 pos)
        {
            bool wasDragging = _dragging;
            float dist = Vector2.Distance(pos, _pressStartPos);
            float dur = Time.time - _pressStartTime;
            _pressing = false;
            if (wasDragging) { EndDrag(); return; }
            if (dist < tapPixelThreshold && dur < tapMaxSeconds) DispatchTap(pos);
        }

        static bool ExtraContains(Vector2 guiPoint) =>
            ExtraBlockRect.HasValue && ExtraBlockRect.Value.Contains(guiPoint);

        void DispatchTap(Vector2 screenPos)
        {
            if (_cam == null) return;
            var ray = _cam.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f));
            if (Physics.Raycast(ray, out var hit, 200f)) OnTapHit?.Invoke(hit);
            else OnTapEmpty?.Invoke();
        }

        void BeginDrag()
        {
            _dragging = true;
            if (BaseGlobe.Instance != null) return; // the globe rig owns the planet's rotation
            if (_spin != null)
            {
                _wasSpinningBeforeDrag = _spin.enabled;
                _spin.enabled = false;
            }
        }

        void EndDrag()
        {
            if (!_dragging) return;
            _dragging = false;
            if (BaseGlobe.Instance != null) { BaseGlobe.Instance.Release(); return; }
            if (_spin != null) _spin.enabled = _wasSpinningBeforeDrag;
        }

        void RotatePlanet(float dx, float dy)
        {
            if (_planet == null) return;
            _planet.Rotate(0f, -dx, 0f, Space.World);
            _planet.Rotate(dy, 0f, 0f, Space.World);
        }

        void Zoom(float delta)
        {
            if (_cam == null) return;
            var pos = _cam.transform.position;
            pos.z = Mathf.Clamp(pos.z + delta, minCameraZ, maxCameraZ);
            _cam.transform.position = pos;
        }
    }
}
