using System;
using System.Collections.Generic;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal sealed class KoffeeEditorKomfort
    {
        internal bool HasShip { get; }
        internal int? CrewCount { get; }
        internal KomfortStatus Komfort { get; }
        internal bool AssumesHomeConnection { get; }

        private KoffeeEditorKomfort(bool hasShip, int? crewCount, KomfortStatus komfort, bool assumesHome = false)
        { HasShip = hasShip; CrewCount = crewCount; Komfort = komfort; AssumesHomeConnection = assumesHome; }

        internal static KoffeeEditorKomfort Capture()
        {
            // ShipManifest is updated by stock's crew callbacks. GetManifest(false)
            // on the dialog itself also writes its seat lists: never invoke it here.
            var ship = HighLogic.LoadedSceneIsEditor ? EditorLogic.fetch?.ship : null;
            if (ship?.parts == null || ship.parts.Count == 0)
                return new KoffeeEditorKomfort(false, null, default);
            Part[] parts = ship.parts.Where(p => p != null && p.State != PartStates.DEAD).Distinct().ToArray();
            VesselCrewManifest? manifest = ShipConstruction.ShipManifest;
            int? count = null;
            if (manifest != null && HighLogic.CurrentGame?.CrewRoster != null)
            {
                var ids = new HashSet<uint>(parts.Select(p => p.craftID));
                count = manifest.PartManifests.Where(p => p != null && ids.Contains(p.PartID))
                    .SelectMany(p => p.GetPartCrew()).Where(c => c != null)
                    .Select(c => c!.name).Distinct(StringComparer.Ordinal).Count();
            }
            return new KoffeeEditorKomfort(true, count, KomfortRuntime.ReadEditorStatus(parts, count),
                KomfortRuntime.EditorHomeAssumption(parts));
        }
    }
}
