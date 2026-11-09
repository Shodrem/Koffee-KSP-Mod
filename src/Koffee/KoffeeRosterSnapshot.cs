using System;
using System.Collections.Generic;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal enum KoffeeRosterGroupKind { Available, Vessel, Unknown, Other }

    internal sealed class KoffeeRosterGroup
    {
        internal KoffeeRosterGroupKind Kind { get; }
        internal Guid VesselId { get; }
        internal string Name { get; }
        internal bool IsEva { get; }
        internal IReadOnlyList<KoffeeCrewStatus> Crew { get; }
        internal KomfortStatus Komfort { get; }
        internal double? AirFraction { get; }
        internal bool AirLastKnown { get; }
        internal string DeadlyWarning { get; }
        internal string Key => Kind == KoffeeRosterGroupKind.Vessel ? "vessel:" + VesselId : Kind.ToString();

        internal KoffeeRosterGroup(KoffeeRosterGroupKind kind, IEnumerable<ProtoCrewMember> crew,
            Guid vesselId = default, string name = "", bool eva = false, KomfortStatus komfort = default,
            double? airFraction = null, bool airLastKnown = false)
        {
            Kind = kind; VesselId = vesselId; Name = name; IsEva = eva; Komfort = komfort;
            AirFraction = airFraction;
            AirLastKnown = airLastKnown;
            DeadlyWarning = eva ? "" : DeadlyScenario.Warning(vesselId);
            Crew = crew.OrderBy(c => c.name, StringComparer.Ordinal).Select(c => new KoffeeCrewStatus(c, name, KerbalKoffeeScenario.Store.TryGetStart(c.name, out double start) &&
                start <= Planetarium.GetUniversalTime() ? Math.Max(0, start + KerbalWorkState.KaffeinatedDuration - Planetarium.GetUniversalTime()) : 0,
                DeadlyScenario.HealthFraction(c.name))).ToArray();
        }
    }

    // The KSC saved fleet is authoritative. Do not consult stale FlightGlobals or
    // ProtoVessel.GetVesselCrew (which calls verifyCrew), and never load a vessel.
    internal sealed class KoffeeRosterSnapshot
    {
        internal static readonly KoffeeRosterSnapshot Empty = new KoffeeRosterSnapshot(null, Array.Empty<KoffeeRosterGroup>());
        internal Game? Game { get; }
        internal IReadOnlyList<KoffeeRosterGroup> Groups { get; }

        private KoffeeRosterSnapshot(Game? game, IReadOnlyList<KoffeeRosterGroup> groups)
        { Game = game; Groups = groups; }

        internal static KoffeeRosterSnapshot Capture()
        {
            Game? game = HighLogic.CurrentGame;
            if (game == null || HighLogic.LoadedScene != GameScenes.SPACECENTER) return Empty;
            var fleet = game.flightState?.protoVessels?.Where(p => p != null).ToArray() ?? Array.Empty<ProtoVessel>();
            var ships = fleet.GroupBy(p => p.vesselID).ToDictionary(g => g.Key, g => g.ToArray());
            var members = new Dictionary<string, ProtoCrewMember>(StringComparer.Ordinal);
            var memberships = new Dictionary<string, HashSet<Guid>>(StringComparer.Ordinal);
            foreach (ProtoVessel ship in fleet)
                if (ship.protoPartSnapshots != null)
                    foreach (ProtoPartSnapshot part in ship.protoPartSnapshots)
                        if (part?.protoModuleCrew != null)
                            foreach (ProtoCrewMember crew in part.protoModuleCrew)
                            {
                                if (crew == null || string.IsNullOrEmpty(crew.name)) continue;
                                if (!members.ContainsKey(crew.name)) members.Add(crew.name, crew);
                                if (!memberships.TryGetValue(crew.name, out var ids))
                                    memberships.Add(crew.name, ids = new HashSet<Guid>());
                                ids.Add(ship.vesselID);
                            }
            // The real roster decides type/status, even if a part holds an old reference.
            foreach (ProtoCrewMember crew in game.CrewRoster?.Kerbals() ?? Enumerable.Empty<ProtoCrewMember>())
                if (crew != null && !string.IsNullOrEmpty(crew.name)) members[crew.name] = crew;

            var available = new List<ProtoCrewMember>();
            var unknown = new List<ProtoCrewMember>();
            var other = new List<ProtoCrewMember>();
            var occupants = new Dictionary<Guid, List<ProtoCrewMember>>();
            foreach (ProtoCrewMember crew in members.Values)
            {
                if ((crew.type != ProtoCrewMember.KerbalType.Crew && crew.type != ProtoCrewMember.KerbalType.Tourist) ||
                    crew.rosterStatus == ProtoCrewMember.RosterStatus.Dead) continue;
                memberships.TryGetValue(crew.name, out var ids);
                if (crew.rosterStatus == ProtoCrewMember.RosterStatus.Available && (ids == null || ids.Count == 0))
                    available.Add(crew);
                else if (crew.rosterStatus == ProtoCrewMember.RosterStatus.Assigned || (ids != null && ids.Count > 0))
                {
                    if (crew.rosterStatus != ProtoCrewMember.RosterStatus.Assigned || ids == null || ids.Count != 1 ||
                        ids.First() == Guid.Empty || ships[ids.First()].Length != 1) { unknown.Add(crew); continue; }
                    Guid id = ids.First();
                    if (!occupants.TryGetValue(id, out var list)) occupants.Add(id, list = new List<ProtoCrewMember>());
                    list.Add(crew);
                }
                else other.Add(crew);
            }
            var groups = new List<KoffeeRosterGroup> { new KoffeeRosterGroup(KoffeeRosterGroupKind.Available, available) };
            foreach (var ship in occupants.OrderBy(g => ships[g.Key][0].vesselName, StringComparer.Ordinal).ThenBy(g => g.Key))
            {
                ProtoVessel proto = ships[ship.Key][0];
                Vessel? vessel = proto.vesselRef;
                // The runtime snapshot must describe this exact proto, not an old ship with the same name/ID.
                KomfortStatus komfort = KomfortRuntime.ReadStatus(vessel != null && vessel.id == proto.vesselID &&
                    ReferenceEquals(vessel.protoVessel, proto) ? vessel : null);
                double? air = DeadlyScenario.AirFraction(proto.vesselID, out bool lastKnown);
                groups.Add(new KoffeeRosterGroup(KoffeeRosterGroupKind.Vessel, ship.Value, proto.vesselID,
                    proto.vesselName ?? "", proto.vesselType == VesselType.EVA, komfort, air, lastKnown));
            }
            if (unknown.Count > 0) groups.Add(new KoffeeRosterGroup(KoffeeRosterGroupKind.Unknown, unknown));
            if (other.Count > 0) groups.Add(new KoffeeRosterGroup(KoffeeRosterGroupKind.Other, other));
            return new KoffeeRosterSnapshot(game, groups.ToArray());
        }
    }

    internal sealed class KoffeeRosterNavigation
    {
        private Game? game;
        private readonly HashSet<string> open = new HashSet<string>(StringComparer.Ordinal);
        internal bool IsOpen(string key) => open.Contains(key);
        internal void Toggle(string key) { if (!open.Remove(key)) open.Add(key); }
        internal bool Reconcile(KoffeeRosterSnapshot snapshot)
        {
            bool changed = !ReferenceEquals(game, snapshot.Game);
            if (changed) { game = snapshot.Game; open.Clear(); }
            open.IntersectWith(snapshot.Groups.Select(g => g.Key));
            return changed;
        }
    }
}
