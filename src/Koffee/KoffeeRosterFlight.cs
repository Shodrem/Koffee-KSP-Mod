using System;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    // Only this explicit command saves or changes scenes. The UI calls it from
    // Update after finishing the IMGUI event, and only with the clicked ID/game.
    internal sealed class KoffeeRosterFlight
    {
        internal bool Starting { get; private set; }

        internal bool TryStart(Game? expectedGame, Guid id, Action releaseWindow, out string errorKey)
        {
            errorKey = "";
            if (Starting) return false;
            Game? game = HighLogic.CurrentGame;
            if (HighLogic.LoadedScene != GameScenes.SPACECENTER || game == null || !ReferenceEquals(game, expectedGame))
            { errorKey = "#Koffee_Crew_FlightGameChanged"; return false; }
            if (!Resolve(game, id, out _, out errorKey)) return false;
            Starting = true;
            try
            {
                // Updated is the same stock preparation invoked by SaveGame(string,...).
                // Its fleet is the sequence FlightState.Load uses to populate FlightGlobals.
                Game updated = game.Updated();
                if (!ReferenceEquals(game, HighLogic.CurrentGame) || HighLogic.LoadedScene != GameScenes.SPACECENTER)
                { Starting = false; errorKey = "#Koffee_Crew_FlightGameChanged"; return false; }
                if (!Resolve(updated, id, out _, out errorKey)) { Starting = false; return false; }
                if (string.IsNullOrEmpty(GamePersistence.SaveGame(updated, "persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE)))
                { Starting = false; errorKey = "#Koffee_Crew_FlightSaveFailed"; return false; }
                // Save callbacks may alter the fleet. Resolve again; never retain a row index.
                if (!Resolve(updated, id, out int index, out errorKey)) { Starting = false; return false; }
                if (!ReferenceEquals(game, HighLogic.CurrentGame) || HighLogic.LoadedScene != GameScenes.SPACECENTER)
                { Starting = false; errorKey = "#Koffee_Crew_FlightGameChanged"; return false; }
                releaseWindow();
                FlightDriver.StartAndFocusVessel(updated, index);
                return true;
            }
            catch (Exception error)
            {
                Starting = false;
                Debug.LogError("[Koffee] Roster flight transition failed: " + error);
                errorKey = "#Koffee_Crew_FlightFailed";
                return false;
            }
        }

        private static bool Resolve(Game game, Guid id, out int index, out string errorKey)
        {
            index = -1;
            errorKey = "#Koffee_Crew_FlightMissing";
            if (id == Guid.Empty || game.flightState?.protoVessels == null) return false;
            if (game.Mode == Game.Modes.MISSION_BUILDER)
            { errorKey = "#Koffee_Crew_FlightMode"; return false; }
            var fleet = game.flightState.protoVessels;
            for (int i = 0; i < fleet.Count; i++)
                if (fleet[i]?.vesselID == id)
                {
                    if (index >= 0) return false;
                    index = i;
                }
            if (index < 0) return false;
            // ProtoVessel.Load skips ships with missing part prefabs. That would
            // shift FlightGlobals and let stock clamp/focus a different ship.
            // Reject this fleet rather than passing an ambiguous index to stock.
            if (!game.compatible || !game.flightState.compatible || fleet.Any(p => p == null ||
                p.vesselID == Guid.Empty ||
                p.protoPartSnapshots == null || p.protoPartSnapshots.Count == 0 ||
                p.protoPartSnapshots.Any(part => part?.partInfo?.partPrefab == null)) ||
                fleet.Select(p => p.vesselID).Distinct().Count() != fleet.Count)
            { errorKey = "#Koffee_Crew_FlightInvalidFleet"; return false; }
            ProtoVessel target = fleet[index];
            // DiscoveryInfo.Load reads DISCOVERY/state as the numeric DiscoveryLevels.
            bool owned = int.TryParse(target.discoveryInfo?.GetValue("state"), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int level) && level == (int)DiscoveryLevels.Owned;
            if (!owned) { errorKey = "#Koffee_Crew_FlightUnowned"; return false; }
            if (target.protoPartSnapshots == null || target.protoPartSnapshots.Count == 0 ||
                !target.protoPartSnapshots.Any(p => p?.protoModuleCrew?.Any(c => c != null) == true)) return false;
            errorKey = "";
            return true;
        }
    }
}
