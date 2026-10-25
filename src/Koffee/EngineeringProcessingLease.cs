using System;
using System.Collections.Generic;

namespace ShoAerospace.Koffee
{
    internal sealed class EngineeringProcessingLease
    {
        private readonly HashSet<EngineeringRenewalCoordinator.PendingRenewal> reservations =
            new HashSet<EngineeringRenewalCoordinator.PendingRenewal>();
        private EngineeringDiagnosticSample? diagnostic;
        private bool progressed;
        private bool settled;
        private double timeFactor;

        internal bool Attach(EngineeringRenewalCoordinator.PendingRenewal reservation) =>
            reservations.Add(reservation);

        internal IEnumerable<EngineeringRenewalCoordinator.PendingRenewal> Reservations() =>
            reservations;

        internal void SetDiagnostic(EngineeringDiagnosticSample sample) => diagnostic = sample;

        internal void Observe(double observedTimeFactor)
        {
            timeFactor = observedTimeFactor;
            if (observedTimeFactor > 1e-9 && EngineeringMath.Finite(observedTimeFactor))
                progressed = true;
        }

        internal void MarkProgress() => Observe(1);

        internal void Complete(Exception? error)
        {
            if (settled) return;
            settled = true;
            EngineeringRenewalCoordinator.Release(this, progressed, error != null);
            if (diagnostic.HasValue)
                EngineeringDiagnostics.Report(diagnostic.Value, progressed, timeFactor, error);
        }

        internal void Cancel()
        {
            if (settled) return;
            settled = true;
            EngineeringRenewalCoordinator.Release(this, false, true);
        }
    }
}
