using System;
using System.Collections.Generic;

namespace ShoAerospace.Koffee
{
    internal sealed class EngineeringProcessingLease
    {
        private readonly HashSet<EngineeringRenewalCoordinator.PendingRenewal> reservations =
            new HashSet<EngineeringRenewalCoordinator.PendingRenewal>();
        private bool progressed;
        private bool settled;

        internal bool Attach(EngineeringRenewalCoordinator.PendingRenewal reservation) =>
            reservations.Add(reservation);

        internal IEnumerable<EngineeringRenewalCoordinator.PendingRenewal> Reservations() =>
            reservations;

        internal void Observe(double observedTimeFactor)
        {
            if (observedTimeFactor > 1e-9 && EngineeringMath.Finite(observedTimeFactor))
                progressed = true;
        }

        internal void MarkProgress() => Observe(1);

        internal void Complete(Exception? error)
        {
            if (settled) return;
            settled = true;
            EngineeringRenewalCoordinator.Release(this, progressed, error != null);
        }

        internal void Cancel()
        {
            if (settled) return;
            settled = true;
            EngineeringRenewalCoordinator.Release(this, false, true);
        }
    }
}
