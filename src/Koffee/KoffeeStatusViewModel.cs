using System;
using System.Collections.Generic;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal readonly struct KoffeeCrewStatus
    {
        internal readonly string Name;
        internal readonly string Profession;
        internal readonly double RemainingSeconds;

        internal bool IsKaffeinated => RemainingSeconds > 0;

        internal KoffeeCrewStatus(string name, string profession, double remainingSeconds)
        {
            Name = name;
            Profession = profession;
            RemainingSeconds = remainingSeconds;
        }
    }

    internal sealed class KoffeeStatusSnapshot
    {
        internal bool HasActiveVessel { get; }
        internal Guid VesselId { get; }
        internal string VesselName { get; }
        internal string Body { get; }
        internal string Situation { get; }
        internal IReadOnlyList<KoffeeCrewStatus> Crew { get; }
        internal double AvailableCups { get; }
        internal IReadOnlyList<KoffeeLabStationStatus> Laboratories { get; }
        internal IReadOnlyList<StationExperimentRuntimeSnapshot> Experiments { get; }
        internal IReadOnlyList<KoffeeScientistStatus> Scientists { get; }
        internal StationSupplyEstimate? Supply { get; }
        internal string EngineerName { get; }
        internal double EngineerCoverage { get; }
        internal double ActivationCost { get; }

        internal KoffeeStatusSnapshot(bool hasActiveVessel,
            IReadOnlyList<KoffeeCrewStatus> crew, double availableCups,
            IReadOnlyList<KoffeeLabStationStatus>? laboratories = null,
            IReadOnlyList<StationExperimentRuntimeSnapshot>? experiments = null,
            IReadOnlyList<KoffeeScientistStatus>? scientists = null,
            StationSupplyEstimate? supply = null, string engineerName = "",
            double engineerCoverage = 0, double activationCost = 1,
            Guid vesselId = default, string vesselName = "", string body = "", string situation = "")
        {
            HasActiveVessel = hasActiveVessel;
            VesselId = vesselId;
            VesselName = vesselName;
            Body = body;
            Situation = situation;
            Crew = crew;
            AvailableCups = availableCups;
            Laboratories = laboratories ?? Array.Empty<KoffeeLabStationStatus>();
            Experiments = experiments ?? Array.Empty<StationExperimentRuntimeSnapshot>();
            Scientists = scientists ?? Array.Empty<KoffeeScientistStatus>();
            Supply = supply;
            EngineerName = engineerName;
            EngineerCoverage = engineerCoverage;
            ActivationCost = activationCost;
        }
    }

    internal static class KoffeeStatusViewModel
    {
        internal static KoffeeStatusSnapshot Capture()
        {
            Dictionary<string, string> assignments = AssignedVesselNames();
            KoffeeScientistStatus[] scientists = HighLogic.CurrentGame?.CrewRoster?.Kerbals()
                .Where(c => c != null && c.trait == ScientistSpecialtyProfile.ProfessionId)
                .Where(c => !HighLogic.LoadedSceneIsEditor || (c.type == ProtoCrewMember.KerbalType.Crew &&
                    (c.rosterStatus == ProtoCrewMember.RosterStatus.Available || c.rosterStatus == ProtoCrewMember.RosterStatus.Assigned)))
                .OrderBy(c => c.name, StringComparer.Ordinal)
                .Select(c => new KoffeeScientistStatus(c, assignments.TryGetValue(c.name, out string? name) ? name : ""))
                .ToArray() ?? Array.Empty<KoffeeScientistStatus>();
            var noVessel = new KoffeeStatusSnapshot(false, Array.Empty<KoffeeCrewStatus>(), 0,
                scientists: scientists);
            if (!HighLogic.LoadedSceneIsFlight || !FlightGlobals.ready) return noVessel;
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null || !vessel.loaded) return noVessel;

            double now = Planetarium.GetUniversalTime();
            KoffeeCrewStatus[] crew = vessel.GetVesselCrew()
                .Where(kerbal => kerbal != null)
                .OrderBy(kerbal => kerbal.name, StringComparer.Ordinal)
                .Select(kerbal => new KoffeeCrewStatus(
                    kerbal.name ?? "",
                    Profession(kerbal),
                    PaidRemaining(kerbal.name ?? "", now)))
                .ToArray();
            KoffeeLabStationStatus[] labs = vessel.parts
                .SelectMany(part => part.Modules.OfType<ModuleKoffeeLab>())
                .OrderBy(lab => lab.part.flightID)
                .Select(lab => new KoffeeLabStationStatus(lab)).ToArray();
            var experiments = StationExperimentRuntime.Capture(vessel);
            var running = experiments.Where(e => e.State?.Status == ExperimentContainerStatus.Running &&
                !e.WorkComplete).ToArray();
            // Mirror Manager priority under the displayed assumption of sufficient supplies.
            // Avoid Select/Windows: their live renewal cache is maintained by the runtime.
            string engineer = ExperimentManagerService.Managers(vessel)
                .Select(m => m.assignedEngineer).FirstOrDefault(name => vessel.GetVesselCrew()
                    .Any(c => c.name == name && ExperimentManagerService.ValidEngineer(c))) ?? "";
            StationSupplyEstimate? supply = null;
            if (running.All(e => e.Research.RemainingSeconds.HasValue) &&
                !string.IsNullOrEmpty(engineer))
            {
                var paid = new Dictionary<string, IReadOnlyList<KaffeinatedWindow>>(StringComparer.Ordinal);
                foreach (string name in running.Select(e => e.State!.AssignedKerbalName).Concat(new[] { engineer }).Distinct())
                {
                    ProtoCrewMember? member = vessel.GetVesselCrew().FirstOrDefault(c => c.name == name);
                    paid[name] = member == null ? Array.Empty<KaffeinatedWindow>() :
                        EngineeringRenewalCoordinator.CaptureWindows(member);
                }
                StationSupplyEstimator.TryEstimate(running.Select(e => new StationSupplyWork(
                    e.State!.AssignedKerbalName, engineer, now, now + e.Research.RemainingSeconds!.Value)).ToArray(),
                    paid, KerbalWorkState.KaffeinatedDuration, out supply);
            }
            double engineerCoverage = PaidRemaining(engineer, now);
            return new KoffeeStatusSnapshot(true, crew, KoffeeSources.Available(vessel),
                labs, experiments, scientists, supply, engineer, engineerCoverage,
                KoffeeSettingsLoader.Current.ActivationCost, vessel.id, vessel.vesselName,
                StationExperimentRuntime.CurrentBody(vessel), StationExperimentRuntime.CurrentSituation(vessel));
        }

        private static Dictionary<string, string> AssignedVesselNames()
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            // In editor, the saved fleet is authoritative; FlightGlobals can still contain the previous scene.
            // Read snapshots directly: ProtoVessel.GetVesselCrew also runs verifyCrew, which is not a UI query.
            var fleet = HighLogic.CurrentGame?.flightState?.protoVessels;
            if (fleet != null)
                foreach (ProtoVessel vessel in fleet)
                    if (vessel?.protoPartSnapshots != null)
                        foreach (ProtoPartSnapshot part in vessel.protoPartSnapshots)
                            if (part?.protoModuleCrew != null)
                                foreach (ProtoCrewMember crew in part.protoModuleCrew)
                                    if (crew != null && !string.IsNullOrEmpty(crew.name))
                                    {
                                        string name = vessel.vesselName ?? "";
                                        // Contradictory membership must not pick an arbitrary ship.
                                        if (result.TryGetValue(crew.name, out string? previous) && previous != name)
                                            result[crew.name] = "";
                                        else if (!result.ContainsKey(crew.name)) result.Add(crew.name, name);
                                    }
            if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && FlightGlobals.Vessels != null)
                foreach (Vessel vessel in FlightGlobals.Vessels)
                    if (vessel != null && vessel.loaded && vessel.parts != null)
                        foreach (Part part in vessel.parts)
                            if (part?.protoModuleCrew != null)
                                foreach (ProtoCrewMember crew in part.protoModuleCrew)
                                    if (crew != null && !string.IsNullOrEmpty(crew.name))
                                        result[crew.name] = vessel.vesselName ?? "";
            return result;
        }

        private static string Profession(ProtoCrewMember kerbal)
        {
            string localized = kerbal.GetLocalizedTrait();
            return string.IsNullOrWhiteSpace(localized) ? kerbal.trait ?? "" : localized;
        }

        private static double PaidRemaining(string name, double now) =>
            KerbalKoffeeScenario.Store.TryGetStart(name, out double start) && start <= now ?
                Math.Max(0, start + KerbalWorkState.KaffeinatedDuration - now) : 0;
    }

    internal sealed class KoffeeScientistStatus
    {
        internal string Name { get; }
        internal int Level { get; }
        internal string Status { get; }
        internal string AssignedVesselName { get; }
        internal ScientistSpecialtyProfile? Profile { get; }
        internal KoffeeScientistStatus(ProtoCrewMember crew, string vesselName)
        {
            Name = crew.name;
            Level = crew.experienceLevel;
            Status = crew.rosterStatus.ToString();
            AssignedVesselName = crew.rosterStatus == ProtoCrewMember.RosterStatus.Assigned ? vesselName : "";
            if (KerbalSpecialtyScenario.TryGetScientistProfile(Name, out var profile)) Profile = profile;
        }
    }

    internal readonly struct KoffeeLabStationStatus
    {
        internal readonly string PartTitle;
        internal readonly int Slots;

        internal KoffeeLabStationStatus(ModuleKoffeeLab lab)
        {
            PartTitle = lab.part.partInfo?.title ?? lab.part.partInfo?.name ?? lab.part.name;
            Slots = lab.StationSlotCapacity;
        }
    }
}
