using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    internal static class EngineeringRenewalCoordinator
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<RenewalKey, PendingRenewal> Pending =
            new Dictionary<RenewalKey, PendingRenewal>();
        private static readonly Dictionary<KerbalIdentity, List<KaffeinatedWindow>> Recent =
            new Dictionary<KerbalIdentity, List<KaffeinatedWindow>>();
        private static int recentFrame = int.MinValue;

        internal static IReadOnlyList<KaffeinatedWindow> Windows(ProtoCrewMember kerbal)
            => ReadWindows(kerbal, true);

        internal static IReadOnlyList<KaffeinatedWindow> CaptureWindows(ProtoCrewMember kerbal)
            => ReadWindows(kerbal, false);

        private static IReadOnlyList<KaffeinatedWindow> ReadWindows(ProtoCrewMember kerbal, bool maintainFrame)
        {
            var identity = new KerbalIdentity(kerbal);
            var result = new List<KaffeinatedWindow>();
            lock (Gate)
            {
                if (maintainFrame) EnsureFrame();
                if (recentFrame == Time.frameCount && Recent.TryGetValue(identity, out List<KaffeinatedWindow>? recent))
                    result.AddRange(recent);
                foreach (PendingRenewal pending in Pending.Values)
                    if (!pending.Settled && pending.Key.Identity.Equals(identity)) result.Add(pending.Window);
            }

            if (KerbalWorkState.TryGetKaffeinatedWindow(
                kerbal, out KaffeinatedWindow stored)) result.Add(stored);
            return result.Where(window => window.EndUT > window.StartUT)
                .OrderBy(window => window.StartUT).ThenBy(window => window.EndUT)
                .GroupBy(window => new { window.StartUT, window.EndUT })
                .Select(group => group.First()).ToArray();
        }

        internal static KaffeinatedWindow? TryReserve(ProtoCrewMember kerbal, Part consumer,
            double activationUT, EngineeringProcessingLease processing)
        {
            var key = new RenewalKey(new KerbalIdentity(kerbal), activationUT);
            lock (Gate)
            {
                EnsureFrame();
                if (Pending.TryGetValue(key, out PendingRenewal? existing))
                {
                    Attach(existing, processing);
                    return existing.Window;
                }

                KaffeinationAttempt? attempt = KerbalWorkState.TryBeginKaffeination(
                    kerbal, consumer, activationUT, false);
                if (attempt == null) return null;
                if (!attempt.NewlyActivated)
                {
                    attempt.Dispose();
                    if (!KerbalWorkState.TryGetKaffeinatedWindow(
                        kerbal, out KaffeinatedWindow active)) return null;
                    Observe(kerbal, active, processing);
                    return active.Contains(activationUT) ? active : (KaffeinatedWindow?)null;
                }

                var pending = new PendingRenewal(key, attempt,
                    new KaffeinatedWindow(activationUT, attempt.UntilUT));
                Pending.Add(key, pending);
                Attach(pending, processing);
                return pending.Window;
            }
        }

        internal static void Observe(ProtoCrewMember kerbal, KaffeinatedWindow window,
            EngineeringProcessingLease processing)
        {
            var identity = new KerbalIdentity(kerbal);
            lock (Gate)
            {
                EnsureFrame();
                foreach (PendingRenewal pending in Pending.Values)
                    if (!pending.Settled && pending.Key.Identity.Equals(identity) &&
                        pending.Window.StartUT == window.StartUT && pending.Window.EndUT == window.EndUT)
                        Attach(pending, processing);
            }
        }

        internal static void Release(EngineeringProcessingLease processing,
            bool progressed, bool failed)
        {
            lock (Gate)
            {
                EnsureFrame();
                foreach (PendingRenewal pending in processing.Reservations()
                    .OrderByDescending(reservation => reservation.Window.StartUT))
                {
                    if (pending.Settled) continue;
                    pending.Progressed |= progressed && !failed;
                    pending.References--;
                    if (pending.References > 0) continue;
                    Pending.Remove(pending.Key);
                    pending.Settled = true;
                    if (pending.Progressed)
                    {
                        pending.Attempt.Commit();
                        if (!Recent.TryGetValue(pending.Key.Identity,
                            out List<KaffeinatedWindow>? windows))
                        {
                            windows = new List<KaffeinatedWindow>();
                            Recent.Add(pending.Key.Identity, windows);
                        }
                        if (!windows.Any(window => window.StartUT == pending.Window.StartUT &&
                            window.EndUT == pending.Window.EndUT)) windows.Add(pending.Window);
                    }
                    pending.Attempt.Dispose();
                }
            }
        }

        internal static void Clear()
        {
            lock (Gate)
            {
                foreach (PendingRenewal pending in Pending.Values
                    .OrderByDescending(reservation => reservation.Window.StartUT).ToArray())
                {
                    if (pending.Settled) continue;
                    pending.Settled = true;
                    pending.Attempt.Dispose();
                }
                Pending.Clear();
                Recent.Clear();
                recentFrame = int.MinValue;
            }
        }

        private static void EnsureFrame()
        {
            int frame = Time.frameCount;
            if (frame == recentFrame) return;
            Recent.Clear();
            recentFrame = frame;
        }

        internal static void RememberCommitted(ProtoCrewMember kerbal, KaffeinatedWindow window)
        {
            var identity = new KerbalIdentity(kerbal);
            lock (Gate)
            {
                EnsureFrame();
                if (!Pending.Values.Any(p => !p.Settled && p.Key.Identity.Equals(identity) &&
                    p.Window.StartUT == window.StartUT)) Remember(identity, window);
            }
        }

        private static void Remember(KerbalIdentity identity, KaffeinatedWindow window)
        {
            if (!Recent.TryGetValue(identity, out List<KaffeinatedWindow>? windows))
            {
                windows = new List<KaffeinatedWindow>();
                Recent.Add(identity, windows);
            }
            if (!windows.Any(w => w.StartUT == window.StartUT && w.EndUT == window.EndUT))
                windows.Add(window);
        }

        private static void Attach(PendingRenewal pending, EngineeringProcessingLease processing)
        {
            if (processing.Attach(pending)) pending.References++;
        }

        internal sealed class PendingRenewal
        {
            internal readonly RenewalKey Key;
            internal readonly KaffeinationAttempt Attempt;
            internal readonly KaffeinatedWindow Window;
            internal int References;
            internal bool Progressed;
            internal bool Settled;

            internal PendingRenewal(RenewalKey key, KaffeinationAttempt attempt,
                KaffeinatedWindow window)
            {
                Key = key;
                Attempt = attempt;
                Window = window;
            }
        }

        internal readonly struct KerbalIdentity : IEquatable<KerbalIdentity>
        {
            private readonly uint persistentId;
            private readonly string name;

            internal KerbalIdentity(ProtoCrewMember kerbal)
            {
                persistentId = kerbal.persistentID;
                name = persistentId == 0 ? kerbal.name ?? "" : "";
            }

            public bool Equals(KerbalIdentity other) => persistentId == other.persistentId &&
                string.Equals(name, other.name, StringComparison.Ordinal);
            public override bool Equals(object? obj) => obj is KerbalIdentity other && Equals(other);
            public override int GetHashCode() => persistentId != 0
                ? persistentId.GetHashCode() : StringComparer.Ordinal.GetHashCode(name);
        }

        internal readonly struct RenewalKey : IEquatable<RenewalKey>
        {
            internal readonly KerbalIdentity Identity;
            private readonly long activationBits;

            internal RenewalKey(KerbalIdentity identity, double activationUT)
            {
                Identity = identity;
                activationBits = BitConverter.DoubleToInt64Bits(activationUT);
            }

            public bool Equals(RenewalKey other) => Identity.Equals(other.Identity) &&
                activationBits == other.activationBits;
            public override bool Equals(object? obj) => obj is RenewalKey other && Equals(other);
            public override int GetHashCode() => Identity.GetHashCode() * 397 ^ activationBits.GetHashCode();
        }
    }
}
