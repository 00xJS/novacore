// iOS/IL2CPP engine-code stripping removes engine classes that no managed code
// references. GameObject.CreatePrimitive attaches its collider by NAME at
// runtime, which the stripper can't see — so SphereCollider/MeshCollider were
// stripped from device builds and every CreatePrimitive logged "Can't add
// component because class 'X' doesn't exist!". The colliders involved are all
// destroyed right after creation (only marker BoxColliders are interactive),
// so this only restores editor/device parity and silences the errors — but any
// future CreatePrimitive that KEEPS its collider would silently break without it.
using UnityEngine;
using UnityEngine.Scripting;

namespace GalaxyRoyale.Game
{
    [Preserve]
    static class StrippingPreserve
    {
        // Never called. The generic AddComponent<T> references are what keep the
        // engine classes alive through the stripper's static analysis.
        [Preserve]
        static void KeepEngineClasses()
        {
            var go = new GameObject();
            go.AddComponent<SphereCollider>();
            go.AddComponent<MeshCollider>();
            go.AddComponent<BoxCollider>();
        }
    }
}
