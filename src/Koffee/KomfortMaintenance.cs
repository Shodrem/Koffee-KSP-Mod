using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal sealed class KomfortMaintenanceState
    {
        internal const string NodeName = "KOFFEE_MAINTENANCE";
        internal double RemainingHours, GrantedHours, ProgressSeconds;
        internal double RepairSeconds, RepairStartHours, RepairTargetHours;
        internal bool LowAutonomyWarned;
        internal double Duration => RepairSeconds > 0 ? RepairSeconds : EngineeringSettingsLoader.Current.MaintenanceSeconds;
        internal double Progress => Math.Max(0, Math.Min(1, ProgressSeconds / Duration));
        internal bool LowAutonomy => RemainingHours <= GrantedHours * .1;
        internal string Engineer = "";
        internal string GrantEngineer = "";
        internal double GrantBonus, GrantKomfort;
        internal bool GrantCaffeinated;
        internal bool InProgress, Paused, KitsPaid;
        internal double CapacityFactor => !EngineeringSettingsLoader.Current.Enabled ? 1 : InProgress ? 0 : RemainingHours > 0 ? 1 : EngineeringSettingsLoader.Current.DegradedCapacity;
        internal static KomfortMaintenanceState Fresh(double baseHours = 0) => new KomfortMaintenanceState {
            RemainingHours = baseHours > 0 ? baseHours : EngineeringSettingsLoader.Current.MaintenanceHours,
            GrantedHours = baseHours > 0 ? baseHours : EngineeringSettingsLoader.Current.MaintenanceHours };
        internal void Use(double seconds)
        {
            if (EngineeringSettingsLoader.Current.Enabled && !InProgress && EngineeringMath.Finite(seconds) && seconds > 0)
                RemainingHours = Math.Max(0, RemainingHours - seconds / 3600);
        }
        internal ConfigNode Save()
        {
            var node = new ConfigNode(NodeName);
            node.AddValue("version", "1");
            foreach (var pair in new[] { ("remainingHours", RemainingHours), ("grantedHours", GrantedHours), ("progressSeconds", ProgressSeconds) })
                node.AddValue(pair.Item1, pair.Item2.ToString("R", CultureInfo.InvariantCulture));
            node.AddValue("repairSeconds", RepairSeconds.ToString("R", CultureInfo.InvariantCulture));
            node.AddValue("repairStartHours", RepairStartHours.ToString("R", CultureInfo.InvariantCulture));
            node.AddValue("repairTargetHours", RepairTargetHours.ToString("R", CultureInfo.InvariantCulture));
            node.AddValue("lowAutonomyWarned", LowAutonomyWarned.ToString());
            node.AddValue("engineer", Engineer); node.AddValue("inProgress", InProgress.ToString());
            node.AddValue("paused", Paused.ToString()); node.AddValue("kitsPaid", KitsPaid.ToString());
            node.AddValue("grantEngineer", GrantEngineer);
            node.AddValue("grantBonus", GrantBonus.ToString("R", CultureInfo.InvariantCulture));
            node.AddValue("grantKomfort", GrantKomfort.ToString("R", CultureInfo.InvariantCulture));
            node.AddValue("grantCaffeinated", GrantCaffeinated.ToString());
            return node;
        }
        internal static bool TryRead(ConfigNode node, out KomfortMaintenanceState state)
        {
            state = new KomfortMaintenanceState();
            bool Number(string key, out double value)
            {
                value = 0;
                return node.GetValues(key).Length == 1 && double.TryParse(node.GetValue(key), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out value) && EngineeringMath.Finite(value) && value >= 0;
            }
            bool Flag(string key, out bool value)
            { value = false; return node.GetValues(key).Length == 1 && bool.TryParse(node.GetValue(key), out value); }
            if (node.GetValues("version").Length != 1 || node.GetValue("version") != "1" ||
                !Number("remainingHours", out state.RemainingHours) || !Number("grantedHours", out state.GrantedHours) ||
                state.GrantedHours <= 0 || state.RemainingHours > state.GrantedHours || !Number("progressSeconds", out state.ProgressSeconds) ||
                !Flag("inProgress", out state.InProgress) || !Flag("paused", out state.Paused) || !Flag("kitsPaid", out state.KitsPaid) ||
                node.GetValues("engineer").Length != 1) return false;
            state.Engineer = node.GetValue("engineer");
            if (node.HasValue("repairSeconds"))
            {
                if (!Number("repairSeconds", out state.RepairSeconds) ||
                    !Number("repairStartHours", out state.RepairStartHours) || !Number("repairTargetHours", out state.RepairTargetHours) ||
                    (state.RepairSeconds > 0 && (state.RepairTargetHours < state.RepairStartHours ||
                     state.RepairTargetHours > state.GrantedHours || state.ProgressSeconds > state.RepairSeconds))) return false;
            }
            if (node.HasValue("lowAutonomyWarned") && !Flag("lowAutonomyWarned", out state.LowAutonomyWarned)) return false;
            if (node.HasValue("grantEngineer"))
            {
                if (node.GetValues("grantEngineer").Length != 1 || !Number("grantBonus", out state.GrantBonus) || state.GrantBonus > 1 ||
                    !Number("grantKomfort", out state.GrantKomfort) || state.GrantKomfort > 1 || !Flag("grantCaffeinated", out state.GrantCaffeinated)) return false;
                state.GrantEngineer = node.GetValue("grantEngineer");
            }
            return !state.InProgress || (state.KitsPaid && !string.IsNullOrEmpty(state.Engineer));
        }
    }

    internal static class KomfortMaintenance
    {
        internal sealed class Unit
        {
            internal uint PartId;
            internal int Index;
            internal bool Enabled, Invalid;
            internal ModuleKomfortService Definition = null!;
            internal KomfortMaintenanceState? State;
            internal Action<KomfortMaintenanceState> Store = null!;
            internal double CapacityFactor => !EngineeringSettingsLoader.Current.Enabled ? 1 : State?.CapacityFactor ?? (Invalid ? EngineeringSettingsLoader.Current.DegradedCapacity : 1);
            internal double SecondsToWear => State?.InProgress == false && State.RemainingHours > 0 ? State.RemainingHours * 3600 : double.PositiveInfinity;
        }
        internal static List<Unit> Capture(Vessel vessel, bool initialize = false)
        {
            var result = new List<Unit>();
            if (vessel.loaded)
            {
                foreach (var part in vessel.parts)
                {
                    if (part == null || part.State == PartStates.DEAD) continue;
                    int index = 0;
                    for (int i = 0; i < part.Modules.Count; i++)
                    {
                        if (!(part.Modules[i] is ModuleKomfortService module)) continue;
                        if (!module.Maintainable) { index++; continue; }
                        if (initialize && module.Maintenance == null && !module.MaintenanceInvalid)
                            module.Maintenance = KomfortMaintenanceState.Fresh(module.MaintenanceBaseHours);
                        result.Add(new Unit { PartId = part.persistentId, Index = index++, Enabled = module.serviceEnabled, Definition = module,
                            State = module.Maintenance, Invalid = module.MaintenanceInvalid, Store = value => module.Maintenance = value });
                    }
                }
            }
            else if (vessel.protoVessel?.protoPartSnapshots != null)
            {
                foreach (var part in vessel.protoVessel.protoPartSnapshots)
                {
                    var definitions = part.partPrefab?.Modules.OfType<ModuleKomfortService>().ToArray() ?? Array.Empty<ModuleKomfortService>();
                    var modules = part.modules.Where(m => m.moduleName == nameof(ModuleKomfortService)).ToArray();
                    for (int i = 0; i < definitions.Length && i < modules.Length; i++)
                    {
                        if (!definitions[i].Maintainable) continue;
                        ConfigNode node = modules[i].moduleValues;
                        var saved = node.GetNodes(KomfortMaintenanceState.NodeName);
                        KomfortMaintenanceState? state = saved.Length == 0 ? (initialize ? KomfortMaintenanceState.Fresh(BaseHours(definitions[i], node)) : null) :
                            saved.Length == 1 && KomfortMaintenanceState.TryRead(saved[0], out var read) ? read : null;
                        Action<KomfortMaintenanceState> store = value => {
                            node.RemoveNodes(KomfortMaintenanceState.NodeName); node.AddNode(value.Save()); };
                        if (initialize && saved.Length == 0 && state != null) store(state);
                        result.Add(new Unit { PartId = part.persistentId, Index = i, Enabled = bool.TryParse(node.GetValue(nameof(ModuleKomfortService.serviceEnabled)), out var on) ? on : definitions[i].serviceEnabled, Definition = definitions[i], State = state, Invalid = saved.Length > 0 && state == null, Store = store });
                    }
                }
            }
            return result;
        }
        private static double BaseHours(ModuleKomfortService definition, ConfigNode node)
        {
            return double.TryParse(node.GetValue(nameof(ModuleKomfortService.maintenanceBaseHours)), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double hours) && EngineeringMath.Finite(hours) && hours > 0
                ? hours : definition.MaintenanceBaseHours;
        }
        internal static double NextBoundary(Vessel vessel, double now, double fraction = 1)
        {
            if (!EngineeringSettingsLoader.Current.Enabled) return double.PositiveInfinity;
            return NextBoundary(vessel, Capture(vessel), now, fraction);
        }

        internal static double NextBoundary(Vessel vessel, IReadOnlyList<Unit> units, double now, double fraction = 1)
        {
            if (!EngineeringSettingsLoader.Current.Enabled) return double.PositiveInfinity;
            double next = double.PositiveInfinity;

            foreach (var unit in units)
            {
                var state = unit.State;
                if (state == null) continue;
                if (state.InProgress && !state.Paused && vessel.loaded && MaintenanceEva.Nearby(unit.Definition.part, state.Engineer) is ProtoCrewMember engineer)
                {
                    double progress = state.RepairSeconds > 0 ? state.Progress : Math.Min(1, state.ProgressSeconds / 600);
                    bool coffee = KerbalWorkState.IsKaffeinatedAt(engineer, now);
                    next = Math.Min(next, now + (1 - progress) * (coffee ? 30 : EngineeringSettingsLoader.Current.MaintenanceSeconds));
                    if (coffee && KerbalWorkState.TryGetKaffeinatedWindow(engineer, out var window)) next = Math.Min(next, window.EndUT);
                }
                else if (unit.Enabled && fraction > 0)
                {
                    next = Math.Min(next, now + unit.SecondsToWear / fraction);
                    if (!state.InProgress && !state.LowAutonomyWarned && !state.LowAutonomy)
                        next = Math.Min(next, now + (state.RemainingHours - state.GrantedHours * .1) * 3600 / fraction);
                }
            }
            return next;
        }
        internal static void PrepareRepair(KomfortMaintenanceState state, string engineer, bool coffee, double komfort, double baseHours = 0)
        {
            double fraction = state.RepairSeconds > 0 ? state.Progress : Math.Min(1, state.ProgressSeconds / 600);
            state.RepairSeconds = coffee ? 30 : EngineeringSettingsLoader.Current.MaintenanceSeconds;
            state.ProgressSeconds = fraction * state.RepairSeconds;
            if (state.RepairTargetHours > 0) return;
            var settings = EngineeringSettingsLoader.Current;
            int score = KerbalSpecialtyScenario.TryGetEngineerProfile(engineer, out var profile) ? profile.Score(EngineeringAffinity.Maintenance) : 0;
            state.GrantBonus = settings.Contribution(score, coffee, komfort, maintenance: true);
            state.GrantEngineer = engineer; state.GrantKomfort = komfort; state.GrantCaffeinated = coffee;
            state.RepairStartHours = state.RemainingHours;
            state.RepairTargetHours = Math.Max(state.RemainingHours, (baseHours > 0 ? baseHours : settings.MaintenanceHours) * (1 + state.GrantBonus));
            state.GrantedHours = state.RepairTargetHours;
        }

        internal static IEnumerable<ProtoCrewMember> Crew(Vessel vessel) => vessel.loaded ? vessel.GetVesselCrew() :
            vessel.protoVessel?.protoPartSnapshots.SelectMany(p => p.protoModuleCrew).Where(c => c != null) ?? Enumerable.Empty<ProtoCrewMember>();

        internal static bool AdvanceRepairs(Vessel vessel, IReadOnlyList<Unit> units, double seconds, double end, bool enabled, double komfort, List<IResourceAccount>? sources = null)
        {
            if (!enabled || !EngineeringSettingsLoader.Current.Enabled || !(seconds > 0) || !vessel.loaded) return false;
            bool completed = false;
            foreach (var unit in units)
            {
                var state = unit.State;
                if (state == null || !state.InProgress || state.Paused || !state.KitsPaid || MaintenanceEva.Nearby(unit.Definition.part, state.Engineer) == null) continue;
                PrepareRepair(state, state.Engineer, EngineeringContribution.CoffeeAt(FlightGlobals.ActiveVessel, state.Engineer, end - seconds), komfort, unit.Definition.MaintenanceBaseHours);
                state.ProgressSeconds = Math.Min(state.Duration, state.ProgressSeconds + seconds);
                state.RemainingHours = Math.Max(state.RemainingHours,
                    state.RepairStartHours + (state.RepairTargetHours - state.RepairStartHours) * state.Progress);
                if (!state.LowAutonomy) state.LowAutonomyWarned = false;
                if (state.ProgressSeconds >= state.Duration)
                {
                    state.RemainingHours = state.RepairTargetHours; state.InProgress = false;
                    state.ProgressSeconds = 0; state.Paused = false; state.KitsPaid = false;
                    state.RepairSeconds = state.RepairStartHours = state.RepairTargetHours = 0;
                    unit.Definition.serviceEnabled = state.RemainingHours > 0 && !DeadlyScenario.Active(vessel);
                    completed = true;
                }
                unit.Store(state);
            }
            return completed;
        }
    }
}
