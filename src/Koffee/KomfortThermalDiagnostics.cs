using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace ShoAerospace.Koffee
{
    // Passive snapshots only: stock flux fields are native integrator diagnostics,
    // not a reconstructed history or simultaneous measurements of this paid slice.
    internal static class KomfortThermalDiagnostics
    {
        // Development opt-in only; normal play skips snapshots and string construction.
        internal static bool Enabled = false;
        internal static Func<double> Clock = () => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        private sealed class Entry
        {
            internal double Next, Energy, Seconds;
            internal double NextPublish, PublishedPower = double.NaN;
            internal string Last = "", State = "";
        }
        private static readonly Dictionary<Part, Entry> entries = new Dictionary<Part, Entry>();
        private static bool failed;
        internal static void Clear() { entries.Clear(); failed = false; }
        private static string N(double value) => value.ToString("G9", CultureInfo.InvariantCulture);
        private static string Text(string value) => value.Replace("\r", " ").Replace("\n", " ");
        internal static void SystemHeatPublished(Part part, double sourcePower, double nativePower, double loopTemperature, int loopId)
        {
            if (!Enabled) return;
            try
            {
                if (!entries.TryGetValue(part, out var entry))
                {
                    if (entries.Count >= 2048) return;
                    entries.Add(part, entry = new Entry());
                }
                double now = Clock();
                if (now < entry.NextPublish && (sourcePower == 0) == (entry.PublishedPower == 0)) return;
                entry.NextPublish = now + 1; entry.PublishedPower = sourcePower;
                UnityEngine.Debug.Log("[Koffee][ClimateHeat] backend=SystemHeat stage=published part=" + part.persistentId +
                    " sampleUT=" + N(Planetarium.GetUniversalTime()) + " sourceKW=" + N(sourcePower) +
                    " nativeSystemKW=" + N(nativePower) + " stepSeconds=" + N(TimeWarp.fixedDeltaTime) +
                    " loopID=" + loopId + " loopK=" + N(loopTemperature) + " outletTargetK=350" +
                    " (AddFlux published; loop temperature is latest native snapshot, not cabin temperature)");
            }
            catch (Exception error) { Failed(error); }
        }
        internal static void Report(Part part, double served, double perKerbal, double seconds, double energy, string state)
        {
            if (!Enabled) return;
            try
            {
                if (!entries.TryGetValue(part, out var entry))
                {
                    if (entries.Count >= 2048) return;
                    entries.Add(part, entry = new Entry());
                }
                entry.Energy += energy; entry.Seconds += seconds;
                double now = Clock();
                if (now < entry.Next && entry.State == state) return;
                entry.State = state;
                bool hot = (part.maxTemp > 0 && part.temperature >= .9 * part.maxTemp) ||
                    (part.skinMaxTemp > 0 && part.skinTemperature >= .9 * part.skinMaxTemp);
                string links = part.ptd == null ? "unavailable" : string.Join(";", part.ptd.thermalLinks.Select(l =>
                    (l.remotePart == null ? "none" : l.remotePart.persistentId.ToString(CultureInfo.InvariantCulture)) +
                    ":contactArea=" + N(l.contactArea)));
                entry.Last = "sampleUT=" + N(Planetarium.GetUniversalTime()) + " vessel=" + part.vessel.id + ":" + Text(part.vessel.vesselName) +
                    " part=" + part.persistentId + " flightID=" + part.flightID + " name=" + Text(part.partInfo?.name ?? part.name) +
                    " parent=" + (part.parent == null ? "none" : part.parent.persistentId.ToString(CultureInfo.InvariantCulture)) +
                    " attach=" + part.attachMode + " loaded=" + part.vessel.loaded + " packed=" + part.vessel.packed +
                    " warp=" + TimeWarp.WarpMode + "/" + N(TimeWarp.CurrentRate) +
                    " served=" + N(served) + " perKerbalKW=" + N(perKerbal) +
                    " sliceSeconds=" + N(seconds) + " sliceAppliedKJ=" + N(energy) +
                    " sliceAppliedKW=" + N(energy / seconds) +
                    " sinceLogAppliedKJ=" + N(entry.Energy) + " sinceLogSliceSeconds=" + N(entry.Seconds) +
                    " internalK=" + N(part.temperature) + " maxInternalK=" + N(part.maxTemp) +
                    " skinK=" + N(part.skinTemperature) + " maxSkinK=" + N(part.skinMaxTemp) +
                    " thermalMass=" + N(part.thermalMass) + " heatConductivity=" + N(part.heatConductivity) +
                    " parentInternalK=" + (part.parent == null ? "unavailable" : N(part.parent.temperature)) +
                    " stockConductionNative=" + N(part.thermalConductionFlux) +
                    " stockConvectionNative=" + N(part.thermalConvectionFlux) +
                    " stockRadiationNative=" + N(part.thermalRadiationFlux) +
                    " thermalLinks=[" + links + "] hot=" + hot + " backend=" + (KomfortSystemHeat.Present ? "SystemHeat" : "Stock") + " thermalState=" + state +
                    " stockLastUT=" + N(part.vessel.lastUT);
                if (KomfortSystemHeat.Present) entry.Last = entry.Last.Replace("Applied", "Queued");
                UnityEngine.Debug.Log("[Koffee][ClimateHeat] " + entry.Last + " (paid slice; SystemHeat energy is queued for native integration; stock fields are latest native snapshots)");
                entry.Energy = entry.Seconds = 0;
                entry.Next = now + (hot ? .2 : 1);
            }
            catch (Exception error) { Failed(error); }
        }
        internal static void Destroyed(Part part)
        {
            if (!Enabled) return;
            try
            {
                if (!entries.TryGetValue(part, out var entry)) return;
                UnityEngine.Debug.Log("[Koffee][ClimateHeat] event=onPartDie eventUT=" + N(Planetarium.GetUniversalTime()) +
                    " part=" + part.persistentId + " internalK=" + N(part.temperature) + " skinK=" + N(part.skinTemperature) +
                    " pendingAppliedKJ=" + N(entry.Energy) + " lastLoggedSnapshot=[" + entry.Last +
                    "] (stock death event; cause not inferred)");
                entries.Remove(part);
            }
            catch (Exception error) { Failed(error); }
        }
        private static void Failed(Exception error)
        {
            if (failed) return;
            failed = true;
            try { UnityEngine.Debug.LogWarning("[Koffee][ClimateHeat] Diagnostics unavailable; heat unchanged: " + error); }
            catch (Exception) { }
        }
    }
}
