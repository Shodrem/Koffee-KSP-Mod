using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using KSP.Localization;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    // No hooks or periodic optical work: all geometry queries occur only on Start.
    internal static class StationRadiationPressureAcquisition
    {
        internal static bool Required(StationExperimentDefinition d) => d.ResultReading == StationResultReading.SolarRadiationPressure;
        internal static string Readiness(StationExperimentDefinition d,Vessel v,Part container,double now)
        {
            var p=StationExperimentReadings.RequiredInstrument(d,v,container);
            if (p == null || p.persistentId == 0 || d.RequiredPartName != StationRadiationPressure.Equipment) return "RPA_Equipment";
            if (!ExperimentContainerState.Finite(now) || now<0 || v.mainBody == null || string.IsNullOrWhiteSpace(v.mainBody.bodyDisplayName) ||
                !StationSolarPublication.TryRead(v,now,out double flux,out bool light,out string source,out bool stockLight)) return "RPA_NotReady";
            if (source != "StockFlightIntegrator" && source != "KopernicusAggregate") return "RPA_Source";
            // An eclipsed primary plus positive aggregate is not darkness; this
            // version cannot attribute multiple sources to a single direction.
            if (light && !stockLight) return "RPA_Source";
            if (!light && flux == 0) return "RPA_NoLight";
            return light && stockLight && ExperimentContainerState.Finite(flux) && flux>0 ? "" : "RPA_NotReady";
        }
        private static CelestialBody? SingleStar(string source,double flux)
        {
            var bodies=FlightGlobals.Bodies;
            if (bodies == null || bodies.Count == 0 || bodies.Count>256 || Planetarium.fetch?.Sun == null) return null;
            var stars=bodies.Where(b=>b != null && b.isStar).ToArray();
            var sun=Planetarium.fetch.Sun;
            if (stars.Length != 1 || stars[0] != sun || string.IsNullOrWhiteSpace(sun.bodyName)) return null;
            Type? type=AccessTools.TypeByName("Kopernicus.Components.KopernicusStar");
            if (source == "StockFlightIntegrator") return type == null ? sun : null;
            // Attribute a completed aggregate only when there is exactly one
            // registered star AND one flux, and its inherited Sun.sun is that body.
            var registered=type == null ? null : AccessTools.Field(type,"Stars")?.GetValue(null) as IList;
            var fluxes=type == null ? null : AccessTools.Field(type,"SolarFlux")?.GetValue(null) as IDictionary;
            if (registered == null || registered.Count != 1 || fluxes == null || fluxes.Count != 1 || registered[0] == null) return null;
            object star=registered[0]!;
            string? name=AccessTools.Field(type!,"StarName")?.GetValue(star) as string;
            return name != null && fluxes.Contains(name) && fluxes[name] is double value && value == flux &&
                ReferenceEquals(AccessTools.Field(type!,"sun")?.GetValue(star),sun) ? sun : null;
        }
        internal static string Illumination(Part p,CelestialBody sun,out double cosine)
        {
            cosine=0;
            if (p.ShieldedFromAirstream) return "RPA_Shielded";
            Transform? axis=p.FindModelTransform("RPA_OpticalAxis");
            if (axis == null || !axis.gameObject.activeInHierarchy) return "RPA_Optics";
            Vector3d offset=sun.position-(Vector3d)axis.position;
            double distance=offset.magnitude;
            if (!ExperimentContainerState.Finite(distance) || !ExperimentContainerState.Finite(sun.Radius) || sun.Radius<=0 || distance<=sun.Radius) return "RPA_NotReady";
            var dir=offset/distance;
            cosine=dir.x*axis.forward.x+dir.y*axis.forward.y+dir.z*axis.forward.z;
            if (!ExperimentContainerState.Finite(cosine) || cosine<=0 || cosine>1.000001) return "RPA_Facing";
            cosine=Math.Min(1,cosine);
            foreach (var b in FlightGlobals.Bodies)
            {
                if (b == sun) continue;
                if (b == null) return "RPA_NotReady";
                Vector3d relative=b.position-(Vector3d)axis.position;
                if (TelescopeOptics.DiskOccluded(offset.x,offset.y,offset.z,sun.Radius,relative.x,relative.y,relative.z,b.Radius)) return "RPA_NoLight";
            }
            int terrain=LayerMask.NameToLayer("TerrainColliders"),local=LayerMask.NameToLayer("Local Scenery");
            if (terrain<0 || local<0) return "RPA_NotReady";
            int mask=LayerUtil.DefaultEquivalent | (1<<terrain) | (1<<local);
            // Five positions on the mirror, 24 mm behind the exported mouth.
            // Keep 1 mm in front of the mirror and do not ignore this part's
            // colliders: its rim/handles can also intercept oblique sunlight.
            foreach (var uv in new[] {new[]{0f,0f},new[]{.05f,0f},new[]{-.05f,0f},new[]{0f,.05f},new[]{0f,-.05f}})
            {
                var position=new Vector3(axis.position.x-axis.forward.x*.024f+axis.right.x*uv[0]+axis.up.x*uv[1],
                    axis.position.y-axis.forward.y*.024f+axis.right.y*uv[0]+axis.up.y*uv[1],
                    axis.position.z-axis.forward.z*.024f+axis.right.z*uv[0]+axis.up.z*uv[1]);
                // Raycasts miss origins inside colliders. A small overlap test
                // rejects that case first. All triggers are excluded explicitly.
                if (Physics.CheckSphere(position,.0005f,mask,QueryTriggerInteraction.Ignore) ||
                    Physics.Raycast(position,(Vector3)dir,(float)Math.Min(float.MaxValue,distance-sun.Radius),mask,QueryTriggerInteraction.Ignore)) return "RPA_Occluded";
            }
            return "";
        }
        internal static bool TryCapture(StationExperimentDefinition d,Vessel v,Part container,Guid run,double now,string body,string situation,
            out StationSolarCapture? capture,out string error)
        {
            capture=null; error="";
            string reason=Readiness(d,v,container,now);
            if (reason == "" && (v.mainBody.bodyName != body || situation != StationExperimentRuntime.CurrentSituation(v) ||
                (situation != "InSpaceLow" && situation != "InSpaceHigh") || !d.AllowsBody(body))) reason="RPA_NotReady";
            if (reason != "") {error=StationSolarAcquisition.Error(reason);return false;}
            if (!StationSolarPublication.TryRead(v,now,out double flux,out _,out string source,out bool stockLight))
            {error=StationSolarAcquisition.Error("RPA_NotReady");return false;}
            var p=StationExperimentReadings.RequiredInstrument(d,v,container)!;
            try
            {
                var star=SingleStar(source,flux);
                if (star == null) reason="RPA_Source";
                else
                {
                    reason=Illumination(p,star,out double cosine);
                    if (reason == "" && (Readiness(d,v,container,now) != "" ||
                        !ReferenceEquals(p,StationExperimentReadings.RequiredInstrument(d,v,container)) ||
                        !StationSolarPublication.TryRead(v,now,out double finalFlux,out _,out string finalSource,out bool finalLight) ||
                        flux != finalFlux || source != finalSource || stockLight != finalLight)) reason="RPA_NotReady";
                    if (reason == "") capture=new StationSolarCapture(run,now,body,v.mainBody.bodyDisplayName,situation,p.persistentId,flux,source,stockLight,
                        StationRadiationPressure.Calculate(flux,star.bodyName,cosine));
                }
            }
            catch (Exception) {reason="RPA_NotReady";}
            if (reason != "") {error=StationSolarAcquisition.Error(reason);return false;}
            return capture != null;
        }
        internal static string ResultText(StationSolarCapture c,string body)
        {
            var p=c.RadiationPressure!;
            string N(double n)=>n.ToString("G10",CultureInfo.CurrentCulture);
            return Localizer.Format("#LOC_Koffee_RadiationPressure_Result",body)+"\n\n"+
                Localizer.Format("#Koffee_RPA_Reading",N(c.Flux),N(p.AbsorbingPa*1e6),N(p.ReflectingPa*1e6),
                    c.UT.ToString("G17",CultureInfo.CurrentCulture),Localizer.Format("#Koffee_Environment_Situation_"+c.Situation));
        }
    }
}
