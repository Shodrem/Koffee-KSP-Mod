using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    // Read-only instrumentation. A receipt belongs to one synchronous recipe
    // scope and is observed after the real Part.RequestResource returns. No
    // resource query, trial recipe, battery delta or replacement broker is used.
    internal static class EngineeringPowerDiagnostics
    {
        internal static bool Enabled = false;
        private static bool errorLogged, limitLogged;
        internal static Func<double> Clock = () => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        [ThreadStatic] private static Sample? current;
        private static Game? game;
        private static GameScenes scene;
        private sealed class Gate
        {
            internal double Next;
            internal int Suppressed, Changes;
            internal string State = "";
            internal readonly Queue<string> Recent = new Queue<string>();
        }
        private static readonly Dictionary<object, Gate> gates = new Dictionary<object, Gate>();
        internal static void Clear() { gates.Clear(); game = null; current = null; errorLogged = limitLogged = false; }
        private static string Number(double value) => value.ToString("G9", CultureInfo.InvariantCulture);
        private static string Text(string value) => value.Replace("\r", " ").Replace("\n", " ").Replace("|", "/");

        internal static Sample? Begin(ConversionRecipe original, ConversionRecipe adjusted, Part part, PartModule module,
            double from, double to, float efficiency, string reason, string engineer = "", EngineeringAffinity affinity = EngineeringAffinity.Systems,
            int score = 0, int level = -1, bool coffee = false, double komfort = 1, double saving = 0)
        {
            if (!Enabled) return null;
            try { return new Sample(original, adjusted, part, module, from, to, efficiency, reason, engineer, affinity, score, level, coffee, komfort, saving); }
            catch (Exception error) { Failed(error); return null; }
        }

        internal sealed class Sample : IDisposable
        {
            private readonly Sample? parent;
            private readonly Part part;
            private readonly PartModule module;
            private readonly double from, to, nominal, adjusted, komfort, saving;
            private readonly float efficiency;
            private readonly string reason, engineer, activity, mode;
            private readonly int score, level, ecId;
            private readonly bool coffee;
            private bool completed, disposed, reported;
            internal bool Deferred;
            private string publication = "stock-processing";
            internal bool InDebit;
            internal double Requested, Received;
            internal int Receipts;
            private double work;
            internal Sample(ConversionRecipe original, ConversionRecipe recipe, Part part, PartModule module, double from, double to,
                float efficiency, string reason, string engineer, EngineeringAffinity affinity, int score, int level, bool coffee, double komfort, double saving)
            {
                this.part = part; this.module = module; this.from = from; this.to = to; this.efficiency = efficiency;
                this.reason = reason; this.engineer = engineer; activity = affinity.ToString(); this.score = score; this.level = level;
                this.coffee = coffee; this.komfort = komfort; this.saving = saving;
                nominal = original.Inputs.Where(r => r.ResourceName == "ElectricCharge").Sum(r => r.Ratio);
                adjusted = recipe.Inputs.Where(r => r.ResourceName == "ElectricCharge").Sum(r => r.Ratio);
                mode = module is BaseConverter converter ? converter.ConverterName : module.GetType().Name;
                ecId = PartResourceLibrary.Instance.GetDefinition("ElectricCharge").id;
                parent = current; current = this;
            }
            internal void Complete(double timeFactor) { work = timeFactor; completed = true; }
            public void Dispose()
            {
                if (disposed) return;
                disposed = true; current = parent;
                if (!Deferred) Report("stock-processing");
            }
            internal void Report(string status)
            {
                try { ReportCore(status); }
                catch (Exception error) { Failed(error); }
            }
            private void ReportCore(string status)
            {
                if (reported || !Enabled) return;
                reported = true; publication = status;
                string result = !completed ? "exception" : work > 0 ? "work" : "blocked";
                string state = reason + "/" + engineer + "/" + Number(saving) + "/" + coffee + "/" + Number(komfort) + "/" + mode + "/" + result + "/" + publication + "/" + StationDbsIntegration.Active + "/" + part.vessel.packed + "/" + TimeWarp.WarpMode + "/" + part.vessel.id;
                Publish(module, state, () =>
                {
                    double elapsed = to - from;
                    return "route=" + (StationDbsIntegration.Active ? "loaded-DBS" : "loaded-stock") +
                        " UT=[" + Number(from) + "," + Number(to) + "] elapsedUT=" + Number(elapsed) + "s" +
                        " vessel=" + part.vessel.id + ":" + Text(part.vessel.vesselName) + " part=" + part.flightID + ":" + part.persistentId + ":" + Text(part.partInfo?.name ?? part.name) +
                        " module=" + module.GetType().Name + "#" + part.Modules.Cast<PartModule>().ToList().IndexOf(module) + ":" + RuntimeHelpers.GetHashCode(module) + " mode=" + Text(mode) +
                        " loaded=" + part.vessel.loaded + " packed=" + part.vessel.packed + " warp=" + TimeWarp.WarpMode + "/" + Number(TimeWarp.CurrentRate) +
                        " activity=" + activity + " engineer=" + Text(engineer) + " score=" + score + " stockLevel=" + level + " coffee=" + coffee +
                        " komfort=" + Number(komfort) + " saving=" + Number(saving) + " reason=" + reason + " result=" + result + " publication=" + publication +
                        " nominalCoeff=" + Number(nominal) + "EC/work-s adjustedCoeff=" + Number(adjusted) + "EC/work-s callerEfficiency=" + Number(efficiency) +
                        " stockPotential=" + Number(nominal * efficiency) + "EC/UT-s(calc,before-other-limits)" +
                        " TimeFactor=" + (completed ? Number(work) : "unavailable") + "work-s(includes-stock-efficiency-and-limits)" +
                        " sameWorkNominalRecipe=" + (completed ? Number(nominal * work) : "unavailable") + "EC(calc)" +
                        " sameWorkAdjustedRecipe=" + (completed ? Number(adjusted * work) : "unavailable") + "EC(calc)" +
                        " physicalRequested=" + (Receipts > 0 ? Number(Requested) : "unobserved") + "EC physicalReceived=" + (Receipts > 0 ? Number(Received) : "unobserved") + "EC receipts=" + Receipts + " observation=" + (Receipts > 0 ? "Part.RequestResource-return" : "no-return-observed") +
                        " noSavingSameWorkPhysicalRequest=" + (adjusted > 0 && Receipts > 0 ? Number(Requested * nominal / adjusted) : "unavailable") + "EC(calc-reference,not-second-run)";
                }, from);
            }
            internal bool Matches(Part source, int resource, double demand) => ReferenceEquals(source, part) && resource == ecId && demand > 0;
        }

        internal sealed class Debit
        {
            internal readonly Sample Owner;
            internal readonly double Requested;
            internal Debit(Sample owner, double requested) { Owner = owner; Requested = requested; }
        }
        internal static void BeforeDebit(Part __instance, int resourceID, double demand, out Debit? __state)
        {
            __state = null;
            if (current == null || current.InDebit || !current.Matches(__instance, resourceID, demand)) return;
            current.InDebit = true; __state = new Debit(current, demand);
        }
        internal static void AfterDebit(Debit? __state, double __result)
        {
            if (__state == null) return;
            __state.Owner.Requested += __state.Requested;
            __state.Owner.Received += __result;
            __state.Owner.Receipts++;
        }
        internal static Exception? FinishDebit(Debit? __state, Exception? __exception)
        {
            if (__state != null) __state.Owner.InDebit = false;
            return __exception;
        }
        internal static void Solver(object consumer, string state, Func<string> detail)
        {
            if (!Enabled) return;
            try { Publish(consumer, state, detail); }
            catch (Exception error) { Failed(error); }
        }
        private static void Failed(Exception error)
        {
            if (errorLogged) return;
            errorLogged = true;
            // A diagnostic failure must not change stock work or refunds.
            try { UnityEngine.Debug.LogWarning("[Koffee][EngineeringPower] Recorder unavailable; processing unchanged: " + error); }
            catch (Exception) { } // Even a broken third-party logger cannot affect the recipe.
        }
        private static void Publish(object consumer, string state, Func<string> detail, double stateUT = double.NaN)
        {
            if (!ReferenceEquals(game, HighLogic.CurrentGame) || scene != HighLogic.LoadedScene)
            { gates.Clear(); limitLogged = false; game = HighLogic.CurrentGame; scene = HighLogic.LoadedScene; }
            if (!gates.TryGetValue(consumer, out var gate))
            {
                if (gates.Count >= 2048)
                {
                    if (!limitLogged) { limitLogged = true; UnityEngine.Debug.LogWarning("[Koffee][EngineeringPower] Diagnostic consumer limit (2048) reached; new consumers omitted until scene/game reset."); }
                    return;
                }
                gates.Add(consumer, gate = new Gate());
            }
            if (gate.State != state)
            {
                gate.Changes++; gate.State = state;
                if (gate.Recent.Count == 4) gate.Recent.Dequeue();
                gate.Recent.Enqueue(Text(state) + (double.IsNaN(stateUT) ? "" : "@UT=" + Number(stateUT)));
            }
            double now = Clock();
            if (now < gate.Next) { gate.Suppressed++; return; }
            UnityEngine.Debug.Log("[Koffee][EngineeringPower] " + detail() + " suppressed=" + gate.Suppressed + " stateChanges=" + gate.Changes +
                (gate.Changes > 1 ? " recentStates=[" + string.Join(";", gate.Recent) + "] (last four only)" : "") +
                " (one exact interval; no averaging across states)");
            gate.Next = now + 8; gate.Suppressed = gate.Changes = 0; gate.Recent.Clear();
        }
    }
}
