using System;

namespace ShoAerospace.Koffee
{
    internal sealed class StationScienceResult
    {
        internal ExperimentContainerState State { get; }
        internal ScienceData Data { get; }

        internal StationScienceResult(ExperimentContainerState state, ScienceData data)
        {
            State = state;
            Data = data;
        }
    }

    internal static class StationScienceResultFactory
    {
        internal static StationScienceResult Create(ExperimentContainerState running,
            StationExperimentDefinition definition, CelestialBody body, double latitude,
            double longitude, uint containerFlightId, double now)
        {
            if (running.Status != ExperimentContainerStatus.Running ||
                !running.WorkComplete() || running.ResearchCategory != definition.ScienceCategory ||
                !string.Equals(running.ExperimentId, definition.Id, StringComparison.Ordinal))
                throw new InvalidOperationException("Station work is not complete.");

            if (!StationSolarAcquisition.Matches(running, definition))
                throw new InvalidOperationException("Required initial solar capture is unavailable or incompatible.");
            if (!StationTelescopeObservation.Matches(running, definition))
                throw new InvalidOperationException("Required telescope acquisition is unavailable or incompatible.");
            if (!StationSurfaceAcquisition.Matches(running, definition))
                throw new InvalidOperationException("Required initial surface capture is unavailable or incompatible.");
            ScienceExperiment experiment = ResearchAndDevelopment.GetExperiment(definition.Id);
            if (experiment == null)
                throw new InvalidOperationException("Science experiment definition is unavailable.");
            if (!Enum.TryParse(running.SolarCapture?.Situation ?? running.CheckpointSituation, false,
                out ExperimentSituations situation))
                throw new InvalidOperationException("Certified science situation is invalid.");
            if (running.SolarCapture != null) body = FlightGlobals.GetBodyByName(running.SolarCapture.Body);
            if (body == null || (running.SolarCapture == null && !string.Equals(body.bodyName, running.CheckpointBody,
                StringComparison.Ordinal)))
                throw new InvalidOperationException("Certified celestial body is unavailable.");

            if (running.HasContextBinding && (running.CheckpointBody != running.BoundBody ||
                running.CheckpointSituation != running.BoundSituation))
                throw new InvalidOperationException("Completed checkpoint does not match its initial scientific context.");
            // Checkpoint/binding above describe the observer's research context.
            // Only the immutable acquisition determines the remote science subject.
            if (StationTelescopeObservation.Required(definition))
            {
                body = FlightGlobals.GetBodyByName(running.TelescopeCapture!.Target);
                if (body == null || body.isStar) throw new InvalidOperationException("Observed body is unavailable.");
                situation = ExperimentSituations.InSpaceHigh;
            }
            if (running.SolarCapture != null)
            {
                body = FlightGlobals.GetBodyByName(running.SolarCapture.Body);
                if (body == null || !Enum.TryParse(running.SolarCapture.Situation, out situation))
                    throw new InvalidOperationException("Initial solar science context is unavailable.");
            }
            string biome = "";
            string biomeDisplay = "";
            if (running.HasSurfaceBinding && (running.CheckpointBody != running.SurfaceBody ||
                running.CheckpointSituation != "SrfLanded"))
                throw new InvalidOperationException("Completed surface checkpoint does not match its initial binding.");
            if (experiment.BiomeIsRelevantWhile(situation))
            {
                if (running.HasSurfaceBinding)
                {
                    biome = running.SurfaceBiome;
                    if (running.SurfaceHasBiome && (body.BiomeMap?.Attributes == null ||
                        !System.Linq.Enumerable.Any(body.BiomeMap.Attributes, a => a != null && a.name == biome)))
                        throw new InvalidOperationException("Certified surface biome is unavailable.");
                }
                else biome = ScienceUtil.GetExperimentBiome(body, latitude, longitude);
                biomeDisplay = ScienceUtil.GetBiomedisplayName(body, biome);
            }
            ScienceSubject subject = ResearchAndDevelopment.GetExperimentSubject(experiment,
                situation, body, biome, biomeDisplay);
            if (subject == null || string.IsNullOrWhiteSpace(subject.id))
                throw new InvalidOperationException("Stock ScienceSubject could not be created.");

            var data = new ScienceData(experiment.baseValue * experiment.dataScale, 1f,
                (float)definition.XmitDataScalar, 0f, subject.id, subject.title, false,
                containerFlightId, running.TelescopeCapture == null ? StationExperimentReadings.ResultText(running, definition) :
                    StationTelescopeObservation.ResultText(running.TelescopeCapture));
            ExperimentContainerState finalized = running.FinalizeRun(subject.id,
                body.bodyName, situation.ToString(), biome, biomeDisplay, now);
            return new StationScienceResult(finalized, data);
        }
    }
}
