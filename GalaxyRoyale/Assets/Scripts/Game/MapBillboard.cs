// Screen-aligned billboard for map markers (2026-07-05). The galaxy map camera
// is a PERSPECTIVE camera pitched off straight-down, so flat sprites lying on
// the ground plane foreshorten into stretched ellipses. Markers with this
// component turn to face the camera every frame, so planets/nodes/fleets STAND
// UP facing the viewer as round orbs — while the tier rings, nebulae and star
// floor stay flat and recede, giving the 3D-universe depth.
//
// The map camera never rotates (only its position dollies on pan/zoom), so the
// billboard rotation is a single shared quaternion MapView refreshes each frame.
using UnityEngine;

namespace GalaxyRoyale.Game
{
    public sealed class MapBillboard : MonoBehaviour
    {
        /// <summary>Current map-camera rotation — set by MapView each frame.</summary>
        public static Quaternion Rotation = Quaternion.identity;

        /// <summary>Extra roll around the view axis (fleets point along heading).</summary>
        public float RollDegrees;

        void LateUpdate()
        {
            transform.rotation = RollDegrees != 0f
                ? Rotation * Quaternion.Euler(0f, 0f, RollDegrees)
                : Rotation;
        }
    }
}
