using System;
using System.Collections.Generic;
using System.Linq;

namespace ShoAerospace.Koffee
{
    // Only observed temporal evidence lives here. No vessel owns a settings copy,
    // no EC balance is simulated, and a new scene/save starts a fresh baseline.
    internal static class KomfortRuntime
    {
        private sealed class Equipment
        {
            internal Part? Part;
            internal string Service = "";
            internal double Capacity, Rate, HeatKilowattsPerKerbal;
            internal uint Id;
            internal bool ThermalReady;
            internal KomfortMaintenance.Unit? Maintenance;
            internal KomfortServiceLife? Life;
        }
        private sealed class Sample
        {
            internal int Crew, Seats, Labs;
            internal bool Home;
            internal DeadlyEnvironment? Environment;
            internal List<KomfortMaintenance.Unit> Maintenance = new List<KomfortMaintenance.Unit>();
            internal readonly List<Equipment> Equipment = new List<Equipment>();
            internal string Identity = "";
        }
        private sealed class Range
        {
            internal double Start, End, Factor;
            internal double Raw(double ut) => Factor;
            internal double At(double ut) => Math.Max(.25, Math.Min(1, Raw(ut)));
            internal double Integral(double from, double to, double lower, double upper) =>
                KomfortRate.Integral(Raw(from), Raw(to), to - from, lower, upper);
        }
        private sealed class Observation
        {
            internal Vessel Vessel = null!;
            internal object? Proto;
            internal Sample Sample = null!;
            internal double UT, FirstUT;
            internal bool Loaded, Electrical;
            internal GameScenes Scene;
            internal readonly List<Range> History = new List<Range>();
            internal CommNet.CommNetVessel? Connection;
            internal object? NetworkSource;
            internal Action? Network;
            internal readonly List<KeyValuePair<double, bool>> HomeChanges = new List<KeyValuePair<double, bool>>();
            internal double? ForecastFraction;
            internal readonly Dictionary<uint, double> Powered = new Dictionary<uint, double>();
        }
        private static readonly Dictionary<Guid, Observation> observations = new Dictionary<Guid, Observation>();
        private static Game? game;
        internal static KomfortGameParameters? Settings => HighLogic.CurrentGame?.Parameters.CustomParams<KomfortGameParameters>();
        internal static KomfortOptions OptionsAt(double ut) => Settings?.At(ut) ?? new KomfortOptions(false, 3, true);
        internal static double SettingsBoundary(double ut) => Math.Min(Settings?.NextBoundary(ut) ?? double.PositiveInfinity, DeadlyScenario.Boundary(ut));

        internal static void Clear()
        {
            foreach (var observation in observations.Values) Unsubscribe(observation);
            observations.Clear(); game = HighLogic.CurrentGame;
            KomfortThermalDiagnostics.Clear();
            KomfortSystemHeat.Clear();
        }
        private static void Unsubscribe(Observation observation)
        {
            if (observation.Connection != null && observation.Network != null)
                observation.Connection.OnNetworkUpdate -= observation.Network;
        }
        internal static void Invalidate(Vessel vessel)
        {
            if (observations.TryGetValue(vessel.id, out var observation)) Unsubscribe(observation);
            observations.Remove(vessel.id);
        }
        internal static void HomeRuleChanged(Vessel vessel, double ut)
        {
            if (observations.TryGetValue(vessel.id, out var observation))
                observation.HomeChanges.Add(new KeyValuePair<double, bool>(ut, Home(vessel)));
        }
        private static bool Home(Vessel vessel) => HighLogic.CurrentGame?.Parameters.Difficulty.EnableCommNet == true &&
            vessel.Connection?.GetType() == typeof(CommNet.CommNetVessel) &&
            vessel.Connection.IsConnectedHome;

        private static Sample Capture(Vessel vessel, bool initializeMaintenance = false)
        {
            // Stock owns the last known home state, including unloaded vessels.
            // Hold it until a stock update; this is not a reconstructed physical
            // link history or a measurement of unobserved orbital geometry.
            var sample = new Sample { Home = Home(vessel), Environment = DeadlyScenario.Tracking(vessel.id) || DeadlyScenario.OptionsAt(Planetarium.GetUniversalTime()).Enabled ? new DeadlyEnvironment(vessel) : null, Maintenance = KomfortMaintenance.Capture(vessel, initializeMaintenance) };
            var identity = new List<string>();
            if (vessel.loaded)
            {
                foreach (var part in vessel.parts.Where(p => p != null && p.State != PartStates.DEAD).Distinct())
                {
                    AddSeats(sample, part, part.CrewCapacity);
                    sample.Crew += part.protoModuleCrew?.Count ?? 0;
                    identity.Add(part.persistentId + ":" + string.Join(",", (part.protoModuleCrew ?? new List<ProtoCrewMember>()).Select(c => c.name)));
                    AddServices(sample, part);
                }
            }
            else if (vessel.protoVessel?.protoPartSnapshots != null)
            {
                foreach (var part in vessel.protoVessel.protoPartSnapshots.Distinct())
                {
                    Part? prefab = part.partPrefab ?? part.partInfo?.partPrefab;
                    if (prefab != null) AddSeats(sample, prefab, SnapshotCapacity(part, prefab));
                    sample.Crew += part.protoModuleCrew?.Count ?? 0;
                    identity.Add(part.persistentId + ":" + string.Join(",", (part.protoModuleCrew ?? new List<ProtoCrewMember>()).Select(c => c.name)));
                    var definitions = prefab?.Modules.OfType<ModuleKomfortService>().ToArray() ?? Array.Empty<ModuleKomfortService>();
                    var saved = part.modules.Where(m => m.moduleName == nameof(ModuleKomfortService)).ToArray();
                    for (int i = 0; i < definitions.Length; i++)
                    {
                        var module = definitions[i];
                        bool on = i < saved.Length && bool.TryParse(saved[i].moduleValues.GetValue(nameof(ModuleKomfortService.serviceEnabled)), out bool enabled)
                            ? enabled : module.serviceEnabled;
                        ConfigNode? lifeValues = i < saved.Length ? saved[i].moduleValues : null;
                        var life = KomfortServiceLife.Capture(module, lifeValues, value =>
                        {
                            if (lifeValues == null)
                            {
                                // Only the coordinator's first Spend migrates a
                                // missing snapshot. Passive UI captures stay pure.
                                var snapshot = new ProtoPartModuleSnapshot(module);
                                part.modules.Add(snapshot); lifeValues = snapshot.moduleValues;
                            }
                            lifeValues.SetValue(nameof(module.lifeRemainingSeconds), value.ToString("R", System.Globalization.CultureInfo.InvariantCulture), true);
                        });
                        if (on && module.Valid) sample.Equipment.Add(new Equipment { Id = part.persistentId, Life = life,
                            Service = module.service, Capacity = module.capacity * (life?.Remaining <= 0 ? 0 : 1) * (sample.Maintenance.FirstOrDefault(u => u.PartId == part.persistentId && u.Index == i)?.CapacityFactor ?? 1),
                            Maintenance = sample.Maintenance.FirstOrDefault(u => u.PartId == part.persistentId && u.Index == i), Rate = life?.Remaining <= 0 ? 0 : module.electricChargePerSecond });
                    }
                }
            }
            identity.AddRange(sample.Maintenance.Select(u => "M:" + u.PartId + ":" + u.Index + ":" +
                u.State?.InProgress + ":" + u.State?.Paused + ":" + u.State?.Engineer));
            identity.Add("O2:" + sample.Environment?.Identity);
            sample.Identity = string.Join(";", identity.OrderBy(s => s)) + "/" + sample.Seats + "/" + sample.Labs + "/" +
                string.Join(";", sample.Equipment.Select(e => e.Id + ":" + e.Service + ":" + e.Capacity + ":" + e.Rate).OrderBy(s => s));
            return sample;
        }
        // Capacity warning only: does not infer electrical supply or advance simulation.
        internal static bool ServiceOverCapacity(Vessel? vessel, string service)
        {
            if (vessel?.loaded != true) return false;
            Sample sample = Capture(vessel);
            double capacity = sample.Equipment.Where(e => e.Service == service).Sum(e => e.Capacity);
            return sample.Crew > 0 && sample.Crew > capacity + 1e-9;
        }

        private static void AddServices(Sample sample, Part part)
        {
            for (int i = 0; i < part.Modules.Count; i++)
            {
                if (!(part.Modules[i] is ModuleKomfortService module) || !module.Valid || !module.serviceEnabled) continue;
                KomfortMaintenance.Unit? maintenance = null;
                foreach (var unit in sample.Maintenance)
                    if (ReferenceEquals(unit.Definition, module)) { maintenance = unit; break; }
                var life = KomfortServiceLife.Capture(module);
                sample.Equipment.Add(new Equipment { Part = part, Id = part.persistentId, Life = life, HeatKilowattsPerKerbal = module.heatKilowattsPerKerbal,
                    ThermalReady = KomfortThermal.Readiness(part) == "ready",
                    Service = module.service, Capacity = module.capacity * (maintenance?.CapacityFactor ?? 1) * (life?.Remaining <= 0 ? 0 : 1),
                    Maintenance = maintenance, Rate = life?.Remaining <= 0 ? 0 : module.electricChargePerSecond });
            }
        }

        // Design estimate, not an electrical or CommNet observation. Use the
        // current craft capacities and assigned manifest count; assume powered
        // enabled services; installed antennas only permit assuming a home link.
        internal static KomfortStatus ReadEditorStatus(IReadOnlyList<Part> parts, int? crew)
        {
            if (HighLogic.CurrentGame == null) return default;
            var options = OptionsAt(Planetarium.GetUniversalTime());
            if (!options.Enabled) return new KomfortStatus(KomfortAvailability.Disabled);
            if (!crew.HasValue) return default;
            if (crew.Value == 0) return new KomfortStatus(KomfortAvailability.NotApplicable);
            var sample = new Sample { Crew = crew.Value,
                Home = EditorHomeAssumption(parts) };
            foreach (Part part in parts.Where(p => p != null && p.State != PartStates.DEAD).Distinct())
            {
                AddSeats(sample, part, part.CrewCapacity);
                AddServices(sample, part);
            }
            return new KomfortStatus(KomfortAvailability.Current, Value(sample, options, _ => 1));
        }

        internal static bool EditorHomeAssumption(IReadOnlyList<Part> parts) =>
            HighLogic.CurrentGame?.Parameters.Difficulty.EnableCommNet == true &&
            parts.Where(p => p != null && p.State != PartStates.DEAD)
                .SelectMany(p => p.Modules.OfType<ModuleDataTransmitter>())
                .Any(a => (a.antennaType == AntennaType.DIRECT || a.antennaType == AntennaType.RELAY) &&
                    ExperimentContainerState.Finite(a.antennaPower) && a.antennaPower > 0);

        private static void AddSeats(Sample sample, Part part, int capacity)
        {
            if (capacity <= 0) return;
            var marker = part.Modules.OfType<ModuleKomfortHabitat>().FirstOrDefault() ??
                part.partInfo?.partPrefab?.Modules.OfType<ModuleKomfortHabitat>().FirstOrDefault();
            if (marker?.eligible == false) return;
            // Stock capsules/habitats have an INTERNAL; declared habitats also
            // cover optional interiors. Labs are identified by functional modules.
            bool lab = marker?.laboratory == true || part.Modules.OfType<ModuleKoffeeLab>().Any() ||
                part.Modules.OfType<ModuleScienceLab>().Any();
            if (lab) sample.Labs += capacity;
            else if (marker != null || part.partInfo?.partConfig?.GetNode("INTERNAL") != null) sample.Seats += capacity;
        }
        private static int SnapshotCapacity(ProtoPartSnapshot snapshot, Part prefab)
        {
            var marker = prefab.Modules.OfType<ModuleKomfortHabitat>().FirstOrDefault();
            if (string.IsNullOrEmpty(marker?.deploymentModule)) return prefab.CrewCapacity;
            var module = snapshot.modules.FirstOrDefault(m => m.moduleName == marker!.deploymentModule);
            // HabUtils persists Deployed, while CrewCapacity itself is not a
            // ProtoPartSnapshot field. Unknown deployment never invents seats.
            if (!bool.TryParse(module?.moduleValues.GetValue("Deployed"), out bool deployed)) return 0;
            return Math.Max(0, deployed ? marker!.deployedCapacity : marker!.retractedCapacity);
        }

        internal static Dictionary<uint, double> BackgroundDemand(Vessel vessel, double ut)
        {
            var result = new Dictionary<uint, double>();
            if (!OptionsAt(ut).Enabled) return result;
            // BRP needs enabled nominal tariffs, not seats, repair snapshots,
            // thermal readiness or a topology signature. Wear never discounts EC.
            bool hasCrew = false;
            void Add(uint id, double rate) => result[id] = (result.TryGetValue(id, out double previous) ? previous : 0) + rate;
            if (vessel.loaded)
            {
                foreach (var part in vessel.parts.Where(p => p != null && p.State != PartStates.DEAD).Distinct())
                {
                    hasCrew |= part.protoModuleCrew?.Count > 0;
                    for (int i = 0; i < part.Modules.Count; i++)
                        if (part.Modules[i] is ModuleKomfortService module && module.Valid && module.serviceEnabled && KomfortServiceLife.RemainingFor(module) > 0)
                            Add(part.persistentId, module.electricChargePerSecond);
                }
            }
            else if (vessel.protoVessel?.protoPartSnapshots != null)
            {
                foreach (var part in vessel.protoVessel.protoPartSnapshots.Distinct())
                {
                    hasCrew |= part.protoModuleCrew?.Count > 0;
                    var definitions = (part.partPrefab ?? part.partInfo?.partPrefab)?.Modules.OfType<ModuleKomfortService>().ToArray()
                        ?? Array.Empty<ModuleKomfortService>();
                    var saved = part.modules.Where(m => m.moduleName == nameof(ModuleKomfortService)).ToArray();
                    for (int i = 0; i < definitions.Length; i++)
                    {
                        var module = definitions[i];
                        bool on = i < saved.Length && bool.TryParse(saved[i].moduleValues.GetValue(nameof(ModuleKomfortService.serviceEnabled)), out bool enabled)
                            ? enabled : module.serviceEnabled;
                        if (on && module.Valid && KomfortServiceLife.RemainingFor(module, i < saved.Length ? saved[i].moduleValues : null) > 0)
                            Add(part.persistentId, module.electricChargePerSecond);
                    }
                }
            }
            if (!hasCrew) result.Clear();
            return result;
        }
        internal static double ServiceRate(Part part, bool editor = false, bool? hasCrew = null)
        {
            double rate = 0;
            for (int i = 0; i < part.Modules.Count; i++)
                if (part.Modules[i] is ModuleKomfortService module && module.Valid && module.serviceEnabled && KomfortServiceLife.RemainingFor(module) > 0)
                    rate += module.electricChargePerSecond;
            if (rate == 0 || !OptionsAt(Planetarium.GetUniversalTime()).Enabled ||
                (!editor && !(hasCrew ?? (part.vessel?.GetVesselCrew().Count != 0)))) return 0;
            return rate;
        }
        internal static double CurrentFactor(Vessel vessel) => ProjectedFactor(vessel, Planetarium.GetUniversalTime());
        internal static double ProjectedFactor(Vessel vessel, double ut)
        {
            var options = OptionsAt(ut);
            if (!options.Enabled || vessel.isEVA && DeadlyScenario.OptionsAt(ut).Enabled) return 1;
            if (ReferenceEquals(game, HighLogic.CurrentGame) &&
                observations.TryGetValue(vessel.id, out var observation) && ReferenceEquals(observation.Vessel, vessel) &&
                ReferenceEquals(observation.Proto, vessel.protoVessel) && observation.Scene == HighLogic.LoadedScene && observation.Loaded == vessel.loaded)
            {
                foreach (var range in observation.History) if (range.Start <= ut && ut < range.End) return range.At(ut);
                if (observation.ForecastFraction.HasValue)
                    return Factor(observation.Sample, options, _ => observation.ForecastFraction.Value, HomeAt(observation, ut));
                if (!StationPowerPolicy.Applies(vessel) && (vessel.loaded || StationBackgroundElectricity.Provider == null))
                    return Factor(observation.Sample, options, _ => 1, HomeAt(observation, ut));
                return Factor(observation.Sample, options, e =>
                    observation.Powered.TryGetValue(e.Id, out double fraction) ? fraction : 0);
            }
            return Factor(Capture(vessel), options, _ => 0);
        }
        internal static void Forecast(Vessel vessel, double fraction)
        {
            if (observations.TryGetValue(vessel.id, out var observation)) observation.ForecastFraction = fraction;
        }
        private static double Factor(Sample sample, KomfortOptions options, Func<Equipment, double> powered, bool? home = null) =>
            KomfortMath.Factor(Value(sample, options, powered, home));

        private static double? Value(Sample sample, KomfortOptions options, Func<Equipment, double> powered, bool? home = null)
        {
            if (sample.Equipment.Any(e => e.Service == "Emergency")) return .1;
            double climate = 0, air = 0, leisure = 0, hygiene = 0;
            foreach (var equipment in sample.Equipment)
            {
                double capacity = equipment.Capacity * powered(equipment);
                switch (equipment.Service)
                {
                    case "Climate": climate += capacity; break;
                    case "Air": air += capacity; break;
                    case "Leisure": leisure += capacity; break;
                    case "Hygiene": hygiene += capacity; break;
                }
            }
            return KomfortMath.Calculate(sample.Crew, sample.Seats, sample.Labs, options,
                climate, air, leisure, home ?? sample.Home, hygiene);
        }

        // Use the same capacities and paid slices as Value. Coverage is vessel-wide.
        // Share capped demand proportionally to avoid counting crew twice.
        private static void ApplyClimateHeat(Sample sample, Func<Equipment, double> supplied, double seconds)
        {
            foreach (var equipment in sample.Equipment)
                if (equipment.Service == "Emergency")
                    KomfortThermal.Apply(equipment.Part, 1, equipment.HeatKilowattsPerKerbal, seconds, equipment.ThermalReady);
            double capacity = 0;
            foreach (var equipment in sample.Equipment)
                if (equipment.Service == "Climate") capacity += equipment.Capacity * supplied(equipment);
            double coverage = capacity > 0 && sample.Crew > 0 ? Math.Min(1, sample.Crew / capacity) : 0;
            foreach (var equipment in sample.Equipment)
                if (equipment.Service == "Climate")
                    KomfortThermal.Apply(equipment.Part, equipment.Capacity * supplied(equipment) * coverage,
                        equipment.HeatKilowattsPerKerbal, seconds, equipment.ThermalReady);
        }

        // A passive query: never Observe, Clear, pay, forecast or publish history here.
        internal static KomfortStatus ReadStatus(Vessel? vessel)
        {
            if (HighLogic.CurrentGame == null) return default;
            double now = Planetarium.GetUniversalTime();
            var options = OptionsAt(now);
            if (!options.Enabled) return new KomfortStatus(KomfortAvailability.Disabled);
            if (vessel == null) return default;
            if (vessel.isEVA && DeadlyScenario.OptionsAt(now).Enabled) return new KomfortStatus(KomfortAvailability.NotApplicable);
            if (!vessel.loaded && (vessel.protoVessel?.protoPartSnapshots == null ||
                vessel.protoVessel.protoPartSnapshots.Any(p => (p.partPrefab ?? p.partInfo?.partPrefab) == null))) return default;
            Sample sample = Capture(vessel);
            if (sample.Crew == 0) return new KomfortStatus(KomfortAvailability.NotApplicable);
            if (sample.Equipment.Any(e => e.Service == "Emergency")) return new KomfortStatus(KomfortAvailability.Current, .1, now);
            bool electrical = vessel.loaded ? StationPowerPolicy.Applies(vessel) : StationBackgroundElectricity.Provider != null;
            // Exemption is runtime policy, never an inference from the battery.
            if (!electrical || sample.Equipment.Count == 0)
                return new KomfortStatus(KomfortAvailability.Current, Value(sample, options, _ => 1));
            if (!ReferenceEquals(game, HighLogic.CurrentGame) ||
                !observations.TryGetValue(vessel.id, out var observation) ||
                !ReferenceEquals(observation.Vessel, vessel) || !ReferenceEquals(observation.Proto, vessel.protoVessel) ||
                observation.Scene != HighLogic.LoadedScene || observation.Loaded != vessel.loaded ||
                observation.Sample.Identity != sample.Identity || observation.UT > now ||
                !options.Equals(OptionsAt(observation.UT)) || (vessel.loaded && observation.Electrical != electrical) ||
                sample.Equipment.Any(e => !observation.Powered.ContainsKey(e.Id))) return default;
            double? value = Value(observation.Sample, options, e => observation.Powered[e.Id],
                HomeAt(observation, observation.UT));
            return new KomfortStatus(observation.UT == now ?
                KomfortAvailability.Current : KomfortAvailability.LastKnown, value, observation.UT);
        }
        private static bool Record(Vessel vessel, Observation observation, double from, double to,
            KomfortOptions options, Func<Equipment, double> supplied, bool home)
        {
            int before = observation.Sample.Crew;
            var sample = observation.Sample;
            if (sample.Environment != null)
                foreach (var slice in sample.Environment.Slices(from, to))
                    DeadlyScenario.Advance(vessel, slice.From, slice.To, options,
                        () => new DeadlyConditions(sample.Crew,
                            sample.Equipment.Where(e => e.Service == "Air").Sum(e => e.Capacity * supplied(e)), slice.Oxygen),
                        count => sample.Crew = count,
                        id => sample.Equipment.Any(e => e.Id == id && e.Service == "Emergency" && supplied(e) >= 1 - 1e-9));
            observation.History.Add(new Range { Start = from, End = to,
                Factor = vessel.isEVA && DeadlyScenario.OptionsAt(from).Enabled ? 1 : Factor(sample, options, supplied, home) });
            return before != sample.Crew;
        }
        internal static double Integral(Vessel vessel, double from, double to, double lower = .25, double upper = 1)
        {
            double total = 0, cursor = from;
            while (cursor < to)
            {
                double end = Math.Min(to, SettingsBoundary(cursor));
                if (!OptionsAt(cursor).Enabled) total += upper * (end - cursor);
                else
                {
                    if (!observations.TryGetValue(vessel.id, out var observation)) throw new InvalidOperationException("No Komfort history.");
                    var range = observation.History.FirstOrDefault(r => r.Start <= cursor && r.End > cursor);
                    if (range == null) throw new InvalidOperationException("Komfort integral has an unobserved gap.");
                    end = Math.Min(end, range.End); total += range.Integral(cursor, end, lower, upper);
                }
                cursor = end;
            }
            return total;
        }
        internal static bool Variable(Vessel vessel, double from, double to) => false;
        internal static double PriceBoundary(Vessel vessel, double ut) => double.PositiveInfinity;
        internal static double QuotedFactor(Vessel vessel, double ut) => ProjectedFactor(vessel, ut);
        private static bool HomeAt(Observation observation, double ut)
        {
            bool home = observation.Sample.Home;
            foreach (var change in observation.HomeChanges) if (change.Key <= ut) home = change.Value;
            return home;
        }

        internal static void Observe(Vessel vessel, double now, bool? closingElectrical = null,
            double? backgroundFraction = null, double? authorizedFrom = null, List<IResourceAccount>? maintenanceAccounts = null)
        {
            if (!ReferenceEquals(game, HighLogic.CurrentGame)) Clear();
            observations.TryGetValue(vessel.id, out var previous);
            // Disabled games retain the old research fast path: no part scan,
            // service transaction or new temporal evidence is required.
            if (!OptionsAt(now).Enabled && (previous == null ||
                (!OptionsAt(previous.UT).Enabled && SettingsBoundary(previous.UT) >= now)))
            {
                if (previous != null) previous.UT = now;
                DeadlyScenario.Baseline(vessel, now);
                return;
            }
            var sample = Capture(vessel, EngineeringSettingsLoader.Current.Enabled);
            if (previous == null || !ReferenceEquals(previous.Vessel, vessel) || !ReferenceEquals(previous.Proto, vessel.protoVessel) ||
                previous.Scene != HighLogic.LoadedScene || !ReferenceEquals(previous.NetworkSource, vessel.Connection) ||
                previous.Loaded != vessel.loaded || now < previous.UT || previous.Sample.Identity != sample.Identity)
            {
                if (previous != null) Unsubscribe(previous);
                long revision = DeadlyScenario.Revision;
                DeadlyScenario.Baseline(vessel, now);
                if (revision != DeadlyScenario.Revision) sample = Capture(vessel, EngineeringSettingsLoader.Current.Enabled);
                previous = new Observation { Vessel = vessel, Proto = vessel.protoVessel, UT = now, FirstUT = now, Loaded = vessel.loaded,
                    Electrical = StationPowerPolicy.Applies(vessel), Sample = sample, Scene = HighLogic.LoadedScene, NetworkSource = vessel.Connection };
                observations[vessel.id] = previous;
                if (vessel.Connection?.GetType() == typeof(CommNet.CommNetVessel))
                {
                    previous.Connection = vessel.Connection;
                    var observed = previous;
                    previous.Network = () =>
                    {
                        if (!ReferenceEquals(game, HighLogic.CurrentGame)) return;
                        double time = Planetarium.GetUniversalTime();
                        if (vessel.loaded) Observe(vessel, time);
                        else
                        {
                            observed.HomeChanges.Add(new KeyValuePair<double, bool>(time, Home(vessel)));
                            if (StationBackgroundElectricity.Provider == null) Observe(vessel, time);
                        }
                    };
                    previous.Connection.OnNetworkUpdate += previous.Network;
                }
            }
            double observedStart = previous.UT;
            double from = observedStart;
            bool processedServices = false;
            while (from < now)
            {
                double to = DeadlyScenario.SafeBoundary(vessel, from, Math.Min(now, SettingsBoundary(from)));
                if (OptionsAt(from).Enabled && previous.Sample.Crew > 0)
                    foreach (var equipment in previous.Sample.Equipment)
                        if (equipment.Life?.Remaining > 0) to = Math.Min(to, from + equipment.Life.Remaining);
                bool hadDemand = previous.Sample.Crew > 0;
                long deadlyRevision = DeadlyScenario.Revision;
                if ((previous.Sample.Crew == 0 || previous.Sample.Equipment.Count == 0) &&
                    !previous.Sample.Maintenance.Any(u => u.State?.InProgress == true))
                {
                    // Empty ships and disabled/absent services still have observed
                    // settings/home boundaries, but no payment, heat, wear or repair.
                    foreach (var change in previous.HomeChanges)
                        if (change.Key > from && change.Key < to) to = change.Key;
                    processedServices |= Record(vessel, previous, from, to, OptionsAt(from), _ => 0, HomeAt(previous, from));
                    foreach (var equipment in previous.Sample.Equipment) previous.Powered[equipment.Id] = 0;
                    from = to;
                    if (deadlyRevision != DeadlyScenario.Revision) { previous.Sample = Capture(vessel); processedServices = true; }
                    continue;
                }
                processedServices = true;
                double repairBoundary = KomfortMaintenance.NextBoundary(vessel, previous.Sample.Maintenance,
                    from, vessel.loaded ? 1 : backgroundFraction ?? 0);
                if (repairBoundary > from) to = Math.Min(to, repairBoundary);
                var options = OptionsAt(from);
                var ends = new Dictionary<Equipment, double>();
                bool subject = vessel.loaded ? (closingElectrical ?? StationPowerPolicy.Applies(vessel)) :
                    backgroundFraction.HasValue || StationBackgroundElectricity.Provider != null;
                StationStockPower? servicePower = null;
                foreach (var group in previous.Sample.Equipment.GroupBy(e => e.Id))
                {
                    var equipment = group.First();
                    double end = from;
                    if (options.Enabled && previous.Sample.Crew > 0)
                    {
                        if (!subject) end = to;
                        else if (!vessel.loaded && backgroundFraction.HasValue) end = to;
                        else if (vessel.loaded && previous.Electrical && equipment.Part != null)
                        {
                            var context = equipment.Part.Modules.OfType<ModuleKomfortService>().First();
                            var power = servicePower ?? (servicePower = StationStockPower.For(context, true, now));
                            using (var payment = power.BeginService(equipment.Part, group.Sum(e => e.Rate), context))
                            { end = from + payment.Authorize(from, to); payment.Commit(); }
                        }
                    }
                    foreach (var unit in group) ends[unit] = end;
                }
                double operatingFraction = !vessel.loaded && subject ? Math.Max(0, Math.Min(1, backgroundFraction ?? 0)) : 1;
                var wearBoundaries = previous.Sample.Equipment.Where(e => e.Maintenance?.State != null && operatingFraction > 0 &&
                    e.Maintenance.SecondsToWear > 0 && e.Maintenance.SecondsToWear / operatingFraction < ends[e] - from)
                    .Select(e => from + e.Maintenance!.SecondsToWear / operatingFraction);
                var boundaries = ends.Values.Concat(wearBoundaries).Where(t => t > from && t < to).Concat(new[] { from, to }).Distinct().OrderBy(t => t).ToArray();
                boundaries = boundaries.Concat(previous.HomeChanges.Select(c => c.Key).Where(t => t > from && t < to))
                    .Distinct().OrderBy(t => t).ToArray();
                if (authorizedFrom.HasValue && authorizedFrom.Value > from && authorizedFrom.Value < to)
                    boundaries = boundaries.Concat(new[] { authorizedFrom.Value }).OrderBy(t => t).ToArray();
                for (int i = 0; i + 1 < boundaries.Length; i++)
                {
                    double begin = boundaries[i];
                    Func<Equipment, double> supplied = e => ends[e] > begin ?
                        (!vessel.loaded && subject ? (begin >= (authorizedFrom ?? from) ? operatingFraction : 0) : 1) *
                        (e.Maintenance?.State?.RemainingHours > 0 && (begin - from) * operatingFraction >= e.Maintenance.SecondsToWear ?
                            EngineeringSettingsLoader.Current.DegradedCapacity : 1) : 0;
                    Record(vessel, previous, begin, boundaries[i + 1], options, supplied, HomeAt(previous, begin));
                    if (vessel.loaded && options.Enabled) ApplyClimateHeat(previous.Sample, supplied, boundaries[i + 1] - begin);
                }
                foreach (var equipment in previous.Sample.Equipment)
                    previous.Powered[equipment.Id] = ends[equipment] >= to ?
                        (!vessel.loaded && subject ? Math.Max(0, Math.Min(1, backgroundFraction ?? 0)) : 1) : 0;
                foreach (var equipment in previous.Sample.Equipment)
                    if (equipment.Maintenance?.State != null)
                    {
                        double begin = !vessel.loaded && subject ? Math.Max(from, authorizedFrom ?? from) : from;
                        bool fresh = equipment.Maintenance.State.RemainingHours > 0;
                        equipment.Maintenance.State.Use(Math.Max(0, ends[equipment] - begin) * operatingFraction);
                        if (fresh && equipment.Maintenance.State.RemainingHours <= 0)
                            equipment.Capacity *= EngineeringSettingsLoader.Current.DegradedCapacity;
                        equipment.Maintenance.Store(equipment.Maintenance.State);
                        KomfortMaintenanceNotice.Observe(vessel, equipment.Maintenance);
                    }
                foreach (var equipment in previous.Sample.Equipment)
                    if (options.Enabled && hadDemand && equipment.Life != null)
                    {
                        equipment.Life.Spend(to - from);
                        if (equipment.Life.Remaining <= 0) { equipment.Capacity = 0; equipment.Rate = 0; }
                    }
                bool repairCompleted = KomfortMaintenance.AdvanceRepairs(vessel, previous.Sample.Maintenance, to - from, to,
                    options.Enabled, DeadlyScenario.OptionsAt(from).Enabled && Continuous(vessel, from, to) ? Integral(vessel, from, to) / (to - from) :
                    Factor(previous.Sample, OptionsAt(to), e =>
                        previous.Powered.TryGetValue(e.Id, out double supplied) ? supplied : 0, HomeAt(previous, to)), maintenanceAccounts);
                from = to;
                if (repairCompleted || deadlyRevision != DeadlyScenario.Revision) previous.Sample = Capture(vessel);
            }
            // Without service/repair processing, nothing here changed the first
            // capture. This also covers duplicate callbacks at exactly the same UT.
            if (processedServices) sample = Capture(vessel);
            if (!vessel.loaded) sample.Home = HomeAt(previous, now);
            previous.HomeChanges.RemoveAll(c => c.Key <= now);
            previous.UT = now; previous.Sample = sample; previous.Electrical = StationPowerPolicy.Applies(vessel);
            // Keep retryable research intervals, plus the latest observation for
            // forecasts. No unbounded per-frame history is needed after publication.
            if (vessel.loaded)
            {
                double floor = vessel.parts.SelectMany(p => p.Modules.OfType<ModuleKoffeeExperiment>())
                    .Where(m => m.State?.Status == ExperimentContainerStatus.Running && !m.State.WorkComplete())
                    .Select(m => m.State!.LastCheckpointUT ?? now).DefaultIfEmpty(now).Min();
                floor = Math.Min(floor, observedStart);
                while (previous.History.Count > 1 && previous.History[0].End < floor) previous.History.RemoveAt(0);
            }
            else if (vessel.protoVessel?.protoPartSnapshots != null)
            {
                // The persisted scientific checkpoint is also the retry floor
                // for proto publication. Parse only the existing temporal key.
                double floor = observedStart;
                foreach (var part in vessel.protoVessel.protoPartSnapshots)
                    foreach (var module in part.modules.Where(m => m.moduleName == nameof(ModuleKoffeeExperiment)))
                        if (double.TryParse(module.moduleValues.GetValue(KoffeeIdentifiers.LastCheckpointUtValue),
                            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double checkpoint) &&
                            ExperimentContainerState.Finite(checkpoint) && checkpoint >= 0) floor = Math.Min(floor, checkpoint);
                while (previous.History.Count > 1 && previous.History[0].End < floor) previous.History.RemoveAt(0);
            }
        }
        internal static bool Continuous(Vessel vessel, double from, double to)
        {
            if (!OptionsAt(from).Enabled && SettingsBoundary(from) >= to) return true;
            if (!observations.TryGetValue(vessel.id, out var observation)) return false;
            double cursor = from;
            while (cursor < to)
            {
                if (!OptionsAt(cursor).Enabled) { cursor = Math.Min(to, SettingsBoundary(cursor)); continue; }
                var range = observation.History.FirstOrDefault(r => r.Start <= cursor && r.End > cursor);
                if (range == null) return false;
                cursor = range.End;
            }
            return cursor >= to;
        }
        internal static double At(Vessel vessel, double ut)
        {
            if (!OptionsAt(ut).Enabled) return 1;
            if (observations.TryGetValue(vessel.id, out var observation))
                foreach (var range in observation.History) if (range.Start <= ut && ut < range.End) return range.At(ut);
            throw new InvalidOperationException("Komfort interval has not been observed.");
        }
        internal static double NextBoundary(Vessel vessel, double ut)
        {
            double next = SettingsBoundary(ut);
            if (OptionsAt(ut).Enabled && observations.TryGetValue(vessel.id, out var maintenanceObservation) && ut >= maintenanceObservation.UT)
                next = Math.Min(next, KomfortMaintenance.NextBoundary(vessel, ut, vessel.loaded ? 1 :
                    maintenanceObservation.ForecastFraction ?? (maintenanceObservation.Powered.Count > 0 ? maintenanceObservation.Powered.Values.Min() : 0)));
            if (OptionsAt(ut).Enabled && observations.TryGetValue(vessel.id, out var observation))
            {
                if (ut >= observation.UT)
                {
                    next = DeadlyScenario.SafeBoundary(vessel, ut, next);
                    if (observation.Sample.Crew > 0)
                        foreach (var equipment in observation.Sample.Equipment)
                            if (equipment.Life?.Remaining > 0) next = Math.Min(next, ut + equipment.Life.Remaining);
                }
                foreach (var range in observation.History) if (range.End > ut) { next = Math.Min(next, range.End); break; }
                foreach (var change in observation.HomeChanges) if (change.Key > ut) next = Math.Min(next, change.Key);
            }
            return Math.Min(next, KoffeeResearchRates.NextBoundary(ut));
        }
    }
}
