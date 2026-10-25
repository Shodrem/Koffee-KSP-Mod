using System;
using System.Linq;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    // Called only by the open viewer, after its render at end of frame. Never by
    // background research, save/load, or a scientific completion callback.
    internal static class TelescopeObservationAcquisition
    {
        internal static string Evaluate(ModuleKoffeeTelescope telescope, ModuleKoffeeExperiment? experiment,
            out TelescopeCapture? capture, bool pairGeometry = false)
        {
            capture = null;
            if (experiment == null || experiment.HasIncompatibleState || experiment.State == null ||
                experiment.vessel != telescope.vessel || experiment.part.State == PartStates.DEAD ||
                !telescope.vessel.parts.Contains(experiment.part) ||
                !experiment.part.Modules.OfType<ModuleKoffeeExperiment>().Contains(experiment) ||
                !StationExperimentCatalogLoader.Current.TryGet(experiment.experimentID, out var definition) ||
                !StationTelescopeObservation.Required(definition) ||
                StationTelescopeObservation.IsPair(definition) != pairGeometry) return "NoExperiment";
            string readiness = StationTelescopeObservation.Readiness(experiment, telescope);
            if (readiness != "") return readiness;
            // Science remainder and run status do not determine optical validity.
            // A damaged run without its initial acquisition cannot be repaired by a later image.
            if (experiment.State.Status != ExperimentContainerStatus.Available && experiment.State.TelescopeCapture == null) return "NoExperiment";
            if (!HighLogic.LoadedSceneIsFlight || telescope.vessel != FlightGlobals.ActiveVessel ||
                !telescope.vessel.loaded || telescope.vessel.packed || MapView.MapIsEnabled ||
                TimeWarp.CurrentRate != 1 || TimeWarp.CurrentRateIndex != 0) return "Unavailable";
            if (telescope.part.partInfo?.name != definition.RequiredPartName) return "InstrumentUnavailable";
            if (!telescope.isActiveAndEnabled || !telescope.isEnabled || telescope.part.State == PartStates.DEAD ||
                !telescope.vessel.parts.Contains(telescope.part)) return "InstrumentUnavailable";
            Transform? optics = telescope.OpticalReference;
            if (optics == null || telescope.part.FindModelTransform(telescope.cameraTransformName) != optics ||
                !optics.gameObject.activeInHierarchy) return "Optics";
            var target = telescope.TargetBody;
            if (target == null || target.isStar || !FlightGlobals.Bodies.Contains(target) ||
                !definition.AllowsBody(target.bodyName)) return "Target";
            string situation = StationExperimentRuntime.CurrentSituation(telescope.vessel);
            if (situation != "InSpaceLow" && situation != "InSpaceHigh") return "Situation";
            if (!telescope.HasCurrentImage || telescope.ViewState != TelescopeViewState.Ready) return "Image";
            var electricity = PartResourceLibrary.Instance.GetDefinition("ElectricCharge");
            if (electricity == null) return "Power";
            telescope.part.GetConnectedResourceTotals(electricity.id, out double amount, out _);
            if (!ExperimentContainerState.Finite(amount) || !(amount > 0)) return "Power";
            if (pairGeometry) return TelescopeOptics.CaptureSettingsValid(telescope.FieldOfView, telescope.captureMaximumErrorDegrees,
                telescope.captureFieldFraction, telescope.captureMinimumDiameterPixels) ? "" : "Geometry";
            Vector3d offset = target.position - (Vector3d)optics.position;
            double distance = offset.magnitude;
            if (!ExperimentContainerState.Finite(distance) || distance <= target.Radius ||
                !TelescopeOptics.CaptureSettingsValid(telescope.FieldOfView, telescope.captureMaximumErrorDegrees,
                    telescope.captureFieldFraction, telescope.captureMinimumDiameterPixels)) return "Geometry";
            double x = Dot(offset, optics.right), y = Dot(offset, optics.up), z = Dot(offset, optics.forward);
            double angularRadius = Math.Asin(target.Radius / distance) * 180 / Math.PI;
            double error = Math.Atan2(Math.Sqrt(x*x + y*y), z) * 180 / Math.PI;
            string failure = TelescopeOptics.CaptureFailure(telescope.FieldOfView, angularRadius, error,
                telescope.captureMaximumErrorDegrees, telescope.captureFieldFraction, telescope.captureMinimumDiameterPixels);
            if (failure != "") return failure;
            foreach (var body in FlightGlobals.Bodies)
            {
                if (body == target) continue;
                Vector3d other = body.position - (Vector3d)optics.position;
                if (TelescopeOptics.DiskOccluded(offset.x, offset.y, offset.z, target.Radius,
                    other.x, other.y, other.z, body.Radius)) return "Occluded";
            }
            // Only celestial bodies can occlude an observation. Bay hardware,
            // reaction wheels and other vessel colliders do not participate.
            try
            {
                capture = new TelescopeCapture(target.bodyName, telescope.vessel.mainBody.bodyName, situation,
                    telescope.vessel.id, telescope.part.persistentId, Planetarium.GetUniversalTime(), x, y, z,
                    target.Radius, telescope.FieldOfView, telescope.captureMaximumErrorDegrees,
                    telescope.captureFieldFraction, telescope.captureMinimumDiameterPixels);
                return "";
            }
            catch (ArgumentException) { return "Geometry"; }
        }

        private static double Dot(Vector3d a, Vector3 b) => a.x*b.x + a.y*b.y + a.z*b.z;
    }
}
