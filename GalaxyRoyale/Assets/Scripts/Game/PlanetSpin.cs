// Slow-rotates the planet on its Y axis so you can see it's a globe.
// Attached programmatically by SceneBootstrap.
using UnityEngine;

namespace GalaxyRoyale.Game
{
    public sealed class PlanetSpin : MonoBehaviour
    {
        [SerializeField] float degreesPerSecond = 6f; // ~1 rotation per minute

        void Update()
        {
            transform.Rotate(0f, degreesPerSecond * Time.deltaTime, 0f, Space.Self);
        }
    }
}
