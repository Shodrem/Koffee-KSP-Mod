using System;
using System.Collections.Generic;
using System.Linq;

namespace ShoAerospace.Koffee
{
    // One session per vessel tick preserves windows needed by later experiments.
    internal sealed class StationKoffeeSession
    {
        private readonly Dictionary<string, History> histories =
            new Dictionary<string, History>(StringComparer.Ordinal);

        internal Transaction Begin(string name, List<IResourceAccount> sources)
        {
            if (!histories.TryGetValue(name, out History? history))
            {
                history = new History(name);
                histories.Add(name, history);
            }
            return new Transaction(history, sources);
        }

        internal Transaction Begin(ProtoCrewMember crew, Part consumer)
        {
            if (!histories.TryGetValue(crew.name, out History? history))
            {
                history = new History(crew.name, crew);
                histories.Add(crew.name, history);
            }
            return new Transaction(history, KoffeeSources.Find(consumer), consumer);
        }

        internal sealed class History
        {
            internal readonly string Name;
            internal readonly ProtoCrewMember? Crew;
            internal readonly List<KaffeinatedWindow> Windows = new List<KaffeinatedWindow>();
            internal History(string name, ProtoCrewMember? crew = null)
            {
                Name = name;
                Crew = crew;
                if (KerbalKoffeeScenario.Store.TryGetStart(name, out double start))
                    Windows.Add(new KaffeinatedWindow(start, start + KerbalWorkState.KaffeinatedDuration));
            }
            internal IReadOnlyList<KaffeinatedWindow> AllWindows => Crew == null ? Windows :
                EngineeringRenewalCoordinator.Windows(Crew);
            internal KaffeinatedWindow? At(double now) => AllWindows
                .Where(window => window.Contains(now)).Cast<KaffeinatedWindow?>().LastOrDefault();
        }

        internal sealed class Transaction : IDisposable
        {
            private readonly History history;
            private readonly List<IResourceAccount> sources;
            private readonly Part? consumer;
            private readonly List<Renewal> renewals = new List<Renewal>();
            private bool settled;
            internal Transaction(History history, List<IResourceAccount> sources, Part? consumer = null)
            {
                this.history = history;
                this.sources = sources;
                this.consumer = consumer;
            }

            internal bool Ensure(double now) => Activate(now).HasValue;

            internal double Coverage(double start, double end, double remaining)
            {
                double cursor = start, credited = 0;
                int guard = 0;
                while (cursor < end && credited < remaining && guard++ < 100000)
                {
                    double next = NextStart(cursor);
                    if (next > cursor) { cursor = next; continue; }
                    if (!Ensure(cursor)) break;
                    double until = Math.Min(end, Math.Min(history.At(cursor)!.Value.EndUT, cursor + remaining - credited));
                    if (!(until > cursor)) break;
                    credited += until - cursor; cursor = until;
                }
                return credited;
            }

            internal bool EnsureTeam(Transaction other, double now)
            {
                int ownMark = renewals.Count, otherMark = other.renewals.Count;
                if (Activate(now).HasValue && other.Activate(now).HasValue) return true;
                other.Rollback(otherMark);
                Rollback(ownMark);
                return false;
            }

            internal double TeamCoverage(Transaction other, double start, double end,
                double remaining, Func<double, double, double>? authorize = null)
            {
                double cursor = start, credited = 0;
                int guard = 0;
                while (cursor < end && credited < remaining && guard++ < 100000)
                {
                    // A later pre-existing activation cannot prove earlier coverage.
                    double next = Math.Max(NextStart(cursor), other.NextStart(cursor));
                    if (next > cursor) { cursor = next; continue; }
                    int ownMark = renewals.Count, otherMark = other.renewals.Count;
                    if (!EnsureTeam(other, cursor)) break;
                    KaffeinatedWindow a = history.At(cursor)!.Value;
                    KaffeinatedWindow b = other.history.At(cursor)!.Value;
                    double until = Math.Min(end, Math.Min(a.EndUT, b.EndUT));
                    until = Math.Min(until, cursor + remaining - credited);
                    if (!(until > cursor)) break;
                    double authorized = authorize == null ? until - cursor : authorize(cursor, until);
                    if (authorized <= 0)
                    {
                        other.Rollback(otherMark); Rollback(ownMark);
                        break;
                    }
                    credited += authorized;
                    cursor += authorized;
                    if (cursor < until) break;
                }
                return credited;
            }

            private double NextStart(double now) => history.At(now).HasValue ? now :
                history.AllWindows.Where(window => window.StartUT > now)
                    .Select(window => window.StartUT).DefaultIfEmpty(now).Min();

            private KaffeinatedWindow? Activate(double now)
            {
                KaffeinatedWindow? active = history.At(now);
                if (history.Crew != null && consumer != null)
                {
                    var lease = new EngineeringProcessingLease();
                    KaffeinatedWindow? live;
                    if (active.HasValue)
                    {
                        EngineeringRenewalCoordinator.Observe(history.Crew, active.Value, lease);
                        live = active;
                    }
                    else if (history.AllWindows.Any(window => window.StartUT > now)) return null;
                    else live = EngineeringRenewalCoordinator.TryReserve(history.Crew, consumer, now, lease);
                    if (!live.HasValue) { lease.Complete(null); return null; }
                    renewals.Add(new Renewal(null, null, live.Value, lease));
                    return live;
                }
                if (active.HasValue) return active;
                if (history.Windows.Any(window => window.StartUT > now)) return null;
                ResourcePayment? payment = ResourcePayment.TryTake(
                    KoffeeSettingsLoader.Current.ActivationCost, sources);
                if (payment == null) return null;
                double? previous = KerbalKoffeeScenario.Store.TryGetStart(history.Name,
                    out double stored) ? stored : (double?)null;
                try
                {
                    KerbalKoffeeScenario.Store.SetStart(history.Name, now);
                    var window = new KaffeinatedWindow(now, now + KerbalWorkState.KaffeinatedDuration);
                    history.Windows.Add(window);
                    renewals.Add(new Renewal(previous, payment, window));
                    return window;
                }
                catch
                {
                    KerbalKoffeeScenario.Store.RestoreStart(history.Name, previous);
                    payment.Dispose();
                    throw;
                }
            }

            internal void Commit()
            {
                if (settled) return;
                foreach (Renewal renewal in renewals)
                {
                    renewal.Payment?.Commit();
                    if (renewal.Lease != null) { renewal.Lease.MarkProgress(); renewal.Lease.Complete(null); }
                }
                settled = true;
            }
            private void Rollback(int mark)
            {
                for (int i = renewals.Count - 1; i >= mark; i--)
                {
                    Renewal renewal = renewals[i];
                    if (renewal.Lease != null) renewal.Lease.Complete(null);
                    else
                    {
                        KerbalKoffeeScenario.Store.RestoreStart(history.Name, renewal.Previous);
                        history.Windows.Remove(renewal.Window);
                        renewal.Payment!.Dispose();
                    }
                    renewals.RemoveAt(i);
                }
            }
            public void Dispose()
            {
                if (settled) return;
                Rollback(0);
                settled = true;
            }
            private readonly struct Renewal
            {
                internal readonly double? Previous;
                internal readonly ResourcePayment? Payment;
                internal readonly KaffeinatedWindow Window;
                internal readonly EngineeringProcessingLease? Lease;
                internal Renewal(double? previous, ResourcePayment? payment, KaffeinatedWindow window,
                    EngineeringProcessingLease? lease = null)
                { Previous = previous; Payment = payment; Window = window; Lease = lease; }
            }
        }
    }
}
