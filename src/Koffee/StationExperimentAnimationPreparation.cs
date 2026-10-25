using System;
using System.Collections.Generic;
using System.Linq;
using KSP.Localization;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    // Transient action attempts, not scientific runs or persisted progress.
    internal static class StationExperimentAnimationPreparation
    {
        internal const string SolarClip = "RSA_Lid_Open";
        private sealed class Attempt
        {
            internal PartModule Requester = null!;
            internal ModuleKoffeeExperiment Owner = null!;
            internal StationExperimentDefinition Definition = null!;
            internal ModuleKoffeeExperimentAnimation Controller = null!;
            internal ExperimentContainerState State = null!;
            internal Vessel Vessel = null!, Active = null!;
            internal object Game = null!;
            internal Part[] Parts = Array.Empty<Part>();
            internal string Body = "", Situation = "";
            internal float Started, OpenPhysics = -1;
        }
        private static readonly Dictionary<ModuleKoffeeExperiment,Attempt> pending = new Dictionary<ModuleKoffeeExperiment,Attempt>();
        internal static string Clip(StationExperimentDefinition d) => d.ResultReading == StationResultReading.SolarIrradiance ? SolarClip : "";
        internal static bool Pending(ModuleKoffeeExperiment owner) => pending.ContainsKey(owner);
        internal static bool Requests(ModuleKoffeeExperimentAnimation controller) => pending.Values.Any(a => ReferenceEquals(a.Controller,controller));
        internal static bool Begin(PartModule requester,ModuleKoffeeExperiment owner,StationExperimentDefinition d,out string error)
        {
            error = "";
            if (owner.State?.Status != ExperimentContainerStatus.Available || !owner.State.HasAssignment)
            { error = Localizer.Format("#Koffee_Station_Error_ContainerUnavailable"); return false; }
            if (Pending(owner)) return true; // Duplicate click joins the same attempt.
            Part? instrument = StationExperimentReadings.RequiredInstrument(d,owner.vessel,owner.part);
            var controller = ModuleKoffeeExperimentAnimation.Find(instrument,Clip(d));
            if (controller == null || !controller.TryState(out _,out _) || owner.vessel.packed ||
                TimeWarp.CurrentRateIndex != 0 || TimeWarp.CurrentRate != 1)
            { error = StationSolarAcquisition.Error("LidClosed"); return false; }
            var vessel = owner.vessel;
            pending.Add(owner,new Attempt {Requester=requester,Owner=owner,Definition=d,Controller=controller,State=owner.State!,
                Vessel=vessel,Active=FlightGlobals.ActiveVessel,Game=HighLogic.CurrentGame,Parts=vessel.parts.ToArray(),
                Body=StationExperimentRuntime.CurrentBody(vessel),Situation=StationExperimentRuntime.CurrentSituation(vessel),Started=Time.realtimeSinceStartup});
            controller.Refresh();
            return true;
        }
        private static bool Valid(Attempt a) => HighLogic.LoadedSceneIsFlight && FlightGlobals.ready &&
            ReferenceEquals(a.Game,HighLogic.CurrentGame) && ReferenceEquals(a.Active,FlightGlobals.ActiveVessel) &&
            a.Owner != null && a.Requester != null && a.Controller != null && a.Owner.part != null &&
            a.Vessel.loaded && !a.Vessel.packed && ReferenceEquals(a.Owner.vessel,a.Vessel) &&
            ReferenceEquals(a.Requester.vessel,a.Vessel) && ReferenceEquals(a.Controller.vessel,a.Vessel) &&
            a.Parts.SequenceEqual(a.Vessel.parts) && a.Vessel.parts.Contains(a.Owner.part) &&
            a.Owner.part.State != PartStates.DEAD && a.Controller.part.State != PartStates.DEAD &&
            ReferenceEquals(a.State,a.Owner.State) && a.State.Status == ExperimentContainerStatus.Available &&
            a.Body == StationExperimentRuntime.CurrentBody(a.Vessel) && a.Situation == StationExperimentRuntime.CurrentSituation(a.Vessel) &&
            ReferenceEquals(StationExperimentReadings.RequiredInstrument(a.Definition,a.Vessel,a.Owner.part),a.Controller.part) &&
            TimeWarp.CurrentRateIndex == 0 && TimeWarp.CurrentRate == 1 && Time.realtimeSinceStartup-a.Started <= 15;
        internal static void Update()
        {
            foreach (var a in pending.Values.ToArray())
            {
                if (!Valid(a) || !a.Controller.TryState(out _,out _)) { Cancel(a.Owner); continue; }
                a.Controller.Refresh();
                if (!a.Controller.Open) { a.OpenPhysics = -1; continue; }
                if (a.OpenPhysics < 0) { a.OpenPhysics = Time.fixedTime; continue; }
                if (Time.fixedTime <= a.OpenPhysics) continue;
                // A completed physical publication AFTER opening is mandatory.
                double now = Planetarium.GetUniversalTime();
                if (!StationSolarPublication.TryRead(a.Vessel,now,out _,out _)) continue;
                pending.Remove(a.Owner);
                bool ok = StationExperimentCommands.Start(a.Requester,a.State.ContainerId,out string error,a.Controller.part);
                a.Controller.Refresh();
                if (!ok) ScreenMessages.PostScreenMessage(error,5,ScreenMessageStyle.UPPER_CENTER);
            }
        }
        internal static void Cancel(ModuleKoffeeExperiment owner)
        {
            if (!pending.TryGetValue(owner,out var a)) return;
            pending.Remove(owner);
            if (a.Controller != null) a.Controller.Refresh();
        }
        internal static void Invalidate(Vessel vessel)
        { foreach (var a in pending.Values.Where(a => ReferenceEquals(a.Vessel,vessel)).ToArray()) Cancel(a.Owner); }
        internal static void Dismiss(Part part)
        { foreach (var a in pending.Values.Where(a => ReferenceEquals(a.Requester.part,part) || ReferenceEquals(a.Owner.part,part)).ToArray()) Cancel(a.Owner); }
        internal static void Clear()
        { foreach (var owner in pending.Keys.ToArray()) Cancel(owner); }
    }
}
