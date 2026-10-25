using System;
using System.Linq;
using KSP.Localization;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    // Selection and a one-frame capture request belong to this open viewer, never to background work.
    internal sealed class TelescopePairSession
    {
        private readonly ModuleKoffeeTelescope telescope;
        private ModuleKoffeeExperiment? experiment;
        private CelestialBody? planet,selected;
        private ExperimentContainerState? requestedState;
        private CelestialBody? requestedMoon;
        private Vector2 scroll;
        private string reason="Select";
        internal CelestialBody? CompanionFor(CelestialBody? target) => target == planet && selected?.referenceBody == target ? selected : null;
        internal bool HasRequest => requestedState != null;
        internal TelescopePairSession(ModuleKoffeeTelescope telescope) { this.telescope=telescope; }
        internal bool Applies(ModuleKoffeeExperiment? candidate) => candidate != null &&
            StationExperimentCatalogLoader.Current.TryGet(candidate.experimentID,out var d) && StationTelescopeObservation.IsPair(d);
        internal void Suspend() { requestedState=null; requestedMoon=null; reason="Image"; }
        internal void Reset() { Suspend(); experiment=null; planet=null; selected=null; }
        internal void Select(CelestialBody moon)
        {
            if (planet == null || moon == planet || moon.isStar || moon.referenceBody != planet || !FlightGlobals.Bodies.Contains(moon)) return;
            selected=moon; Suspend();
        }
        internal bool RequestCapture()
        {
            if (reason != "" || requestedState != null || selected == null || experiment?.State == null ||
                experiment.State.TelescopeCapture != null || !telescope.HasCurrentImage || telescope.TargetBody != planet) return false;
            requestedState=experiment.State; requestedMoon=selected; return true;
        }
        internal void Frame(ModuleKoffeeExperiment? candidate)
        {
            if (!Applies(candidate)) { Reset(); return; }
            if (candidate != experiment || planet != telescope.TargetBody)
            { Reset(); experiment=candidate; planet=telescope.TargetBody; }
            if (selected == null) { Suspend(); reason="Select"; return; }
            reason=Evaluate(selected,out var sample);
            if (!HasRequest) return;
            var state=requestedState!;
            bool valid=reason == "" && sample != null && selected == requestedMoon && ReferenceEquals(state,experiment!.State);
            Suspend();
            if (!valid) { reason="Rejected"; return; }
            var evidence=new TelescopePairEvidence(planet!.bodyName,sample!,telescope.pairMaximumSeparationRadii,
                telescope.pairMinimumPlanetDiameterPixels,telescope.pairMinimumMoonDiameterPixels);
            var s=evidence.Sample;
            var capture=new TelescopeCapture(selected.bodyName,telescope.vessel.mainBody.bodyName,
                StationExperimentRuntime.CurrentSituation(telescope.vessel),telescope.vessel.id,telescope.part.persistentId,
                s.UT,s.MX,s.MY,s.MZ,s.MoonRadius,s.Fov,telescope.captureMaximumErrorDegrees,
                telescope.captureFieldFraction,telescope.captureMinimumDiameterPixels,pair:evidence);
            reason=experiment!.TryRecordTelescopeCapture(state.ContainerId,state.Revision,capture) ? "Saved" : "Rejected";
        }
        private string Evaluate(CelestialBody moon,out TelescopePairSample? sample)
        {
            sample=null;
            if (planet == null || moon == planet || planet.isStar || moon.isStar || moon.referenceBody != planet ||
                !FlightGlobals.Bodies.Contains(planet) || !FlightGlobals.Bodies.Contains(moon)) return "Target";
            var optics=telescope.OpticalReference;
            if (optics == null) return "Optics";
            Vector3d p=planet.position-(Vector3d)optics.position,m=moon.position-(Vector3d)optics.position;
            try
            {
                sample=new TelescopePairSample(telescope.SampleUniversalTime,Dot(p,optics.right),Dot(p,optics.up),Dot(p,optics.forward),
                    Dot(m,optics.right),Dot(m,optics.up),Dot(m,optics.forward),planet.Radius,moon.Radius,telescope.FieldOfView);
            }
            catch (ArgumentException) { return "Geometry"; }
            string geometry=sample.Failure(telescope.pairMaximumSeparationRadii,telescope.pairMinimumPlanetDiameterPixels,telescope.pairMinimumMoonDiameterPixels);
            if (geometry != "") return geometry;
            foreach (var other in FlightGlobals.Bodies)
            {
                if (other == planet || other == moon) continue;
                Vector3d o=other.position-(Vector3d)optics.position;
                if (TelescopeOptics.DiskOccluded(p.x,p.y,p.z,planet.Radius,o.x,o.y,o.z,other.Radius) ||
                    TelescopeOptics.DiskOccluded(m.x,m.y,m.z,moon.Radius,o.x,o.y,o.z,other.Radius)) return "Overlap";
            }
            string common=TelescopeObservationAcquisition.Evaluate(telescope,experiment,out _,true);
            if (common != "") return common;
            if (!StationExperimentCatalogLoader.Current.TryGet(experiment!.experimentID,out var definition) || !definition.AllowsBody(moon.bodyName)) return "Target";
            return experiment.State!.TelescopeCapture == null ? "" : experiment.State.TelescopeCapture.Target == moon.bodyName ? "Saved" : "Bound";
        }
        private static double Dot(Vector3d a,Vector3 b) => a.x*b.x+a.y*b.y+a.z*b.z;
        private string Message(string key)
        {
            if (key == "") key="Ready";
            bool own=key=="Select" || key=="Field" || key=="Far" || key=="Overlap" || key=="Small" ||
                key=="Ready" || key=="Completed" || key=="Saved" || key=="Bound" || key=="Rejected";
            return Localizer.Format((own ? "#Koffee_Pair_Reason_" : "#Koffee_Telescope_Reason_")+key);
        }
        internal void Draw(Rect area)
        {
            GUI.Box(area,Localizer.Format("#Koffee_Pair_Moons"));
            var moons=planet != null && planet == telescope.TargetBody ? FlightGlobals.Bodies
                .Where(b => b != planet && !b.isStar && b.referenceBody == planet).ToArray() : Array.Empty<CelestialBody>();
            scroll=GUI.BeginScrollView(new Rect(area.x+6,area.y+30,area.width-12,420),scroll,new Rect(0,0,area.width-36,Math.Max(410,moons.Length*92)));
            for (int i=0;i<moons.Length;i++)
            {
                var moon=moons[i];
                if (GUI.Button(new Rect(0,i*92,area.width-38,26),(moon == selected ? "> " : "")+TelescopeBodyNames.Display(moon))) Select(moon);
                string state=Evaluate(moon,out _);
                var records=telescope.vessel.parts.Where(p => p != null && p.State != PartStates.DEAD)
                    .SelectMany(p => p.Modules.OfType<ModuleKoffeeExperiment>()).Where(e => e.experimentID == experiment?.experimentID &&
                        e.TelescopeObservations.Any(c => c.Target == moon.bodyName && c.Pair != null)).ToArray();
                var subject=ResearchAndDevelopment.GetSubjectByID(experiment?.experimentID+"@"+moon.name+TelescopeCapture.ScienceSituation);
                bool done=subject != null && subject.science >= subject.scienceCap-1e-5 ||
                    records.Any(e => e.State?.Status == ExperimentContainerStatus.Finalized || e.State?.Status == ExperimentContainerStatus.Spent);
                if (done) state="Completed";
                else if (records.Length > 0) state="Saved";
                GUI.Label(new Rect(0,i*92+28,area.width-38,60),Message(state));
            }
            GUI.EndScrollView();
            GUI.Label(new Rect(area.x+10,area.y+470,area.width-20,125),Localizer.Format("#Koffee_Pair_Conditions",
                telescope.pairMaximumSeparationRadii.ToString("G4"),telescope.pairMinimumPlanetDiameterPixels.ToString("G4"),
                telescope.pairMinimumMoonDiameterPixels.ToString("G4")));
            GUI.Label(new Rect(area.x+10,area.y+606,area.width-20,100),Message(reason));
            bool enabled=GUI.enabled;
            GUI.enabled=enabled && reason == "" && !HasRequest && telescope.HasCurrentImage;
            if (GUI.Button(new Rect(area.x+10,area.y+720,area.width-20,36),Localizer.Format("#Koffee_Pair_Capture"))) RequestCapture();
            GUI.enabled=enabled;
        }
    }
}
