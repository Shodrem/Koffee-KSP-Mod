using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    internal static class EngineeringKoffeeService
    {
        private static bool errorLogged;
        private sealed class Baseline
        {
            internal Game? Game;
            internal Vessel Vessel = null!;
            internal object? Proto;
            internal string Signature = "";
            internal double UT;
        }
        private static readonly Dictionary<BaseConverter, Baseline> baselines = new Dictionary<BaseConverter, Baseline>();
        private static readonly HashSet<PartModule> processing = new HashSet<PartModule>();
        private sealed class MonitorSample
        {
            internal Game? Game;
            internal Vessel Vessel = null!;
            internal object? Proto;
            internal double UT, Factor;
        }
        private static readonly Dictionary<BaseConverter, MonitorSample> monitor = new Dictionary<BaseConverter, MonitorSample>();
        internal static double MonitorFactor(BaseConverter converter)
        {
            double now = Planetarium.GetUniversalTime();
            return EngineeringSettingsLoader.Current.Enabled && converter.IsActivated && monitor.TryGetValue(converter, out var sample) &&
                ReferenceEquals(sample.Game, HighLogic.CurrentGame) && ReferenceEquals(sample.Vessel, converter.vessel) &&
                ReferenceEquals(sample.Proto, converter.vessel?.protoVessel) && sample.Vessel.loaded &&
                now >= sample.UT && now - sample.UT <= Math.Max(1, TimeWarp.fixedDeltaTime * 1.5) ? sample.Factor : 1;
        }
        internal static void ForgetMonitor(BaseConverter converter) => monitor.Remove(converter);

        // Execute real stock recipes at temporal boundaries. Each input/output
        // keeps its stock efficiency; only the copied EC input is discounted.
        internal static bool TryProcess(ResourceConverter stock, double seconds, ConversionRecipe original,
            Part part, PartModule module, float efficiency, out ConverterResults result)
            => TryProcess(stock, seconds, original, part, module, efficiency, out result, out _);

        internal static bool IsProcessing(PartModule module) => processing.Contains(module);
        internal static bool TryProcess(ResourceConverter stock, double seconds, ConversionRecipe original,
            Part part, PartModule module, float efficiency, out ConverterResults result, out string reason)
        {
            result = default;
            reason = EngineeringSettingsLoader.Current.Enabled ? "ineligible" : "disabled";
            if (processing.Contains(module) || !Eligible(seconds, part, module, efficiency, out var converter)) return false;
            var candidates = EngineeringWorkAssignment.Relevant(converter!);
            double now = Planetarium.GetUniversalTime();
            double end = ConverterTiming.UpdateEndUT(converter!);
            if (!EngineeringMath.Finite(end) || end <= 0 || end > now + 1) end = now;
            double start = end - seconds;
            KomfortRuntime.Observe(part.vessel, end);
            string signature = string.Join(";", candidates.Select(c => c.Name + ":" + c.Score)) + "/" +
                string.Join(",", part.vessel.parts.Where(p => p.State != PartStates.DEAD).Select(p => p.persistentId + ":" + p.flightID));
            baselines.TryGetValue(converter!, out var previous);
            baselines[converter!] = new Baseline { Game = HighLogic.CurrentGame, Vessel = part.vessel,
                Proto = part.vessel.protoVessel, Signature = signature, UT = end };
            if (candidates.Count == 0) { reason = "no-eligible-engineer-profile"; return false; }
            if (previous == null) { reason = "first-baseline"; return false; }
            if (!ReferenceEquals(previous.Game, HighLogic.CurrentGame) || !ReferenceEquals(previous.Vessel, part.vessel) ||
                !ReferenceEquals(previous.Proto, part.vessel.protoVessel) || previous.Signature != signature)
            { reason = "identity-crew-or-topology-change"; return false; }
            if (start < 0 || start < previous.UT - 1e-6 || end < previous.UT || !KomfortRuntime.Continuous(part.vessel, start, end))
            { reason = "unobserved-interval"; return false; }
            if (!original.Inputs.Any(r => r.ResourceName == "ElectricCharge" && r.Ratio > 0))
            { reason = "no-EC-input"; return false; }
            reason = "observed-interval";
            var chosen = candidates[0];
            processing.Add(module);
            double cursor = start, authorized = 0, savedWork = 0;
            try
            {
                int guard = 0;
                while (cursor < end && guard++ < 100000)
                {
                    double boundary = Math.Min(end, KomfortRuntime.NextBoundary(part.vessel, cursor));
                    double komfort = KomfortRuntime.At(part.vessel, cursor);
                    var settings = EngineeringSettingsLoader.Current;
                    using (var lease = new EngineeringLeaseScope())
                    {
                        var windows = EngineeringRenewalCoordinator.Windows(chosen.Worker);
                        var active = windows.Where(w => w.Contains(cursor)).Cast<KaffeinatedWindow?>().LastOrDefault();
                        bool useful = settings.Contribution(chosen.Score, true, komfort) > settings.Contribution(chosen.Score, false, komfort);
                        if (!active.HasValue && useful && !windows.Any(w => w.StartUT > cursor) && EngineeringContribution.MayBuyOptional(part.vessel, part, cursor))
                            active = EngineeringRenewalCoordinator.TryReserve(chosen.Worker, part, cursor, lease.Lease);
                        if (active.HasValue)
                        {
                            if (useful) EngineeringRenewalCoordinator.Observe(chosen.Worker, active.Value, lease.Lease);
                            boundary = Math.Min(boundary, active.Value.EndUT);
                        }
                        else foreach (var window in windows) if (window.StartUT > cursor) boundary = Math.Min(boundary, window.StartUT);
                        if (!(boundary > cursor)) break;
                        var affinity = converter!.GetType() == typeof(ModuleResourceHarvester) ? EngineeringAffinity.Extraction : EngineeringAffinity.Refining;
                        bool variable = KomfortRuntime.Variable(part.vessel, cursor, boundary);
                        double saving = variable ? EngineeringContribution.AverageSavings(part.vessel, chosen.Name, affinity, cursor, boundary) :
                            settings.Contribution(chosen.Score, active.HasValue, komfort);
                        var recipe = Discount(original, saving);
                        KomfortVariablePowerBroker? weighted = null;
                        if (variable && original.Inputs.Count(r => r.ResourceName == "ElectricCharge") == 1)
                        {
                            double rate = original.Inputs.Single(r => r.ResourceName == "ElectricCharge").Ratio;
                            double begin = cursor;
                            weighted = new KomfortVariablePowerBroker(StationDbsIntegration.Active ? (IResourceBroker)new StationDbsResourceBroker() : new ResourceBroker(),
                                rate, (boundary - cursor) * efficiency, work => rate * work *
                                    (1 - EngineeringContribution.AverageSavings(part.vessel, chosen.Name, affinity, begin, Math.Min(boundary, begin + work / efficiency))));
                        }
                        using (var sample = EngineeringPowerDiagnostics.Begin(original, recipe, part, module, cursor, boundary, efficiency,
                            saving > 0 ? "discount" : "zero-contribution", chosen.Name,
                            converter!.GetType() == typeof(ModuleResourceHarvester) ? EngineeringAffinity.Extraction : EngineeringAffinity.Refining,
                            chosen.Score, chosen.Worker.experienceLevel, active.HasValue, komfort, saving))
                        {
                            var segment = weighted == null ? stock.ProcessRecipe(boundary - cursor, recipe, part, module, efficiency) :
                                new ResourceConverter(weighted).ProcessRecipe(boundary - cursor, original, part, module, efficiency);
                            lease.Lease.Observe(segment.TimeFactor);
                            lease.Success();
                            sample?.Complete(segment.TimeFactor);
                            result = segment;
                            authorized += segment.TimeFactor;
                            savedWork += (variable && segment.TimeFactor > 0 ? EngineeringContribution.AverageSavings(part.vessel, chosen.Name, affinity,
                                cursor, Math.Min(boundary, cursor + segment.TimeFactor / efficiency)) : saving) * segment.TimeFactor;
                            // Stock TimeFactor already includes efficiencyBonus. It
                            // is work, not elapsed UT; do not stop a level-0 .25x
                            // converter at an otherwise fully processed boundary.
                            if (segment.TimeFactor < (boundary - cursor) * efficiency - 1e-8) break;
                            cursor = boundary;
                        }
                    }
                }
                result.TimeFactor = authorized;
                monitor[converter!] = new MonitorSample { Game = HighLogic.CurrentGame, Vessel = part.vessel, Proto = part.vessel.protoVessel,
                    UT = end, Factor = authorized > 0 ? 1 - savedWork / authorized : 1 };
                return true;
            }
            catch (Exception error)
            {
                monitor.Remove(converter!);
                if (!errorLogged) { errorLogged = true; Debug.LogError("[Koffee] Engineering recipe failed: " + error); }
                // A stock exception may follow real withdrawals. Never replay it.
                throw;
            }
            finally { processing.Remove(module); }
        }
        internal static ConversionRecipe Discount(ConversionRecipe original, double saving)
        {
            if (!(saving > 0) || !EngineeringMath.Finite(saving)) return original;
            var copy = new ConversionRecipe { FillAmount = original.FillAmount, TakeAmount = original.TakeAmount };
            foreach (var input in original.Inputs)
                copy.Inputs.Add(input.ResourceName == "ElectricCharge" ?
                    new ResourceRatio(input.ResourceName, input.Ratio * (1 - Math.Min(.95, saving)), input.DumpExcess, input.FlowMode) : input);
            copy.Outputs.AddRange(original.Outputs); copy.Requirements.AddRange(original.Requirements);
            return copy;
        }
        private sealed class EngineeringLeaseScope : IDisposable
        {
            internal readonly EngineeringProcessingLease Lease = new EngineeringProcessingLease();
            private bool success;
            internal void Success() { success = true; Lease.Complete(null); }
            public void Dispose() { if (!success) Lease.Cancel(); }
        }
        private static bool Eligible(double seconds, Part part, PartModule module, float efficiency, out BaseConverter? converter)
        {
            converter = module as BaseConverter;
            return EngineeringSettingsLoader.Current.Enabled && HighLogic.LoadedSceneIsFlight && part != null && module != null &&
                converter != null && ReferenceEquals(module.part, part) && part.partInfo != null && part.State != PartStates.DEAD &&
                part.vessel?.loaded == true && converter.IsActivated && seconds > 0 && EngineeringMath.Finite(seconds) &&
                efficiency > 0 && EngineeringMath.Finite(efficiency) && EngineeringModuleFilter.Matches(converter.GetType(),
                    part.partInfo.name, converter.UseSpecialistBonus, converter.ExperienceEffect);
        }
        internal static void Clear()
        {
            baselines.Clear(); processing.Clear(); monitor.Clear(); errorLogged = false;
            EngineeringPowerDiagnostics.Clear();
            EngineeringRenewalCoordinator.Clear();
        }
    }

    internal static class EngineeringModuleFilter
    {
        internal static bool Matches(Type exactType, string partName,
            bool useSpecialistBonus, string experienceEffect)
        {
            if (!useSpecialistBonus || string.IsNullOrEmpty(partName) ||
                !TryFamily(exactType, out EngineeringModuleFamily family)) return false;
            return EngineeringTargetLoader.Current.Matches(family, partName, experienceEffect);
        }

        internal static bool TryFamily(Type exactType, out EngineeringModuleFamily family)
        {
            if (exactType == typeof(ModuleResourceHarvester))
            {
                family = EngineeringModuleFamily.ResourceHarvester;
                return true;
            }
            if (exactType == typeof(ModuleResourceConverter))
            {
                family = EngineeringModuleFamily.ResourceConverter;
                return true;
            }
            family = default;
            return false;
        }
    }
}
