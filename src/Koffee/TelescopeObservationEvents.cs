using System;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    // Published after ownership by the ED state (saved at the next game save). Future consumers can use
    // identity and acquisition evidence without consulting science remainder.
    public static class TelescopeObservationEvents
    {
        public static event Action<Guid, string, TelescopeCapture>? Captured;

        internal static void Publish(Guid containerId, string experimentId, TelescopeCapture capture)
        {
            var handlers = Captured;
            if (handlers == null) return;
            foreach (Action<Guid, string, TelescopeCapture> handler in handlers.GetInvocationList())
                try { handler(containerId, experimentId, capture); }
                catch (Exception e) { Debug.LogError("[Koffee] Telescope observation listener failed after recording: " + e); }
        }
    }
}
