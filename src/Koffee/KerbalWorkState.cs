using System;
using System.Globalization;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    public static class KerbalWorkState
    {
        public static double KaffeinatedDuration => KoffeeSettingsLoader.Current.ActivationDuration;

        public static bool IsKaffeinated(ProtoCrewMember kerbal) =>
            GetKaffeinatedRemaining(kerbal) > 0;

        public static double GetKaffeinatedRemaining(ProtoCrewMember kerbal)
            => GetKaffeinatedRemaining(kerbal, Planetarium.GetUniversalTime());

        internal static double GetKaffeinatedRemaining(ProtoCrewMember kerbal, double universalTime)
        {
            if (kerbal == null) return 0;
            bool active = KerbalKoffeeScenario.Store.TryGetRemaining(kerbal.name,
                universalTime, KaffeinatedDuration,
                out double remaining, out bool expired);
            if (expired) Debug.Log("[Koffee] " + kerbal.name + " Kaffeinated expired.");
            return active && remaining > 0 && !double.IsNaN(remaining) &&
                !double.IsInfinity(remaining) ? remaining : 0;
        }

        internal static bool TryGetKaffeinatedWindow(ProtoCrewMember kerbal,
            out KaffeinatedWindow window)
        {
            if (kerbal != null && KerbalKoffeeScenario.Store.TryGetStart(
                kerbal.name, out double startUT))
            {
                double untilUT = startUT + KaffeinatedDuration;
                if (!double.IsNaN(untilUT) && !double.IsInfinity(untilUT))
                {
                    window = new KaffeinatedWindow(startUT, untilUT);
                    return true;
                }
            }
            window = default;
            return false;
        }

        internal static double GetKaffeinatedUntil(ProtoCrewMember kerbal) =>
            TryGetKaffeinatedWindow(kerbal, out KaffeinatedWindow window) ? window.EndUT : 0;

        internal static bool IsKaffeinatedAt(ProtoCrewMember kerbal, double universalTime)
        {
            return TryGetKaffeinatedWindow(kerbal, out KaffeinatedWindow window) &&
                window.Contains(universalTime);
        }

        public static bool TryKaffeinate(ProtoCrewMember kerbal, Part consumer)
        {
            try
            {
                Part? source = EvaKoffeeSources.Consumer(consumer);
                using (KaffeinationAttempt? attempt = TryBeginKaffeination(kerbal, source, true))
                {
                    if (attempt == null) return false;
                    attempt.Commit();
                    return true;
                }
            }
            catch (Exception error)
            {
                Debug.LogError("[Koffee] Kaffeination failed and was rolled back: " + error);
                return false;
            }
        }

        internal static KaffeinationAttempt? TryBeginKaffeination(ProtoCrewMember kerbal,
            Part? consumer, bool logDetails)
            => TryBeginKaffeination(kerbal, consumer, Planetarium.GetUniversalTime(), logDetails);

        internal static KaffeinationAttempt? TryBeginKaffeination(ProtoCrewMember kerbal,
            Part? consumer, double activationUT, bool logDetails)
        {
            if (kerbal == null || string.IsNullOrEmpty(kerbal.name)) return null;
            if (IsKaffeinatedAt(kerbal, activationUT))
            {
                double remaining = GetKaffeinatedUntil(kerbal) - activationUT;
                if (logDetails) Debug.Log("[Koffee] " + kerbal.name +
                    " already Kaffeinated, remaining " + FormatDuration(remaining));
                return KaffeinationAttempt.AlreadyActive(kerbal);
            }

            if (logDetails) Debug.Log("[Koffee] " + kerbal.name + " needs Koffee.");
            if (consumer == null) return null;
            // A renewal must not erase coverage another worker still needs this frame.
            if (TryGetKaffeinatedWindow(kerbal, out KaffeinatedWindow previousWindow))
                EngineeringRenewalCoordinator.RememberCommitted(kerbal, previousWindow);
            ResourcePayment? payment = ResourcePayment.TryTake(
                KoffeeSettingsLoader.Current.ActivationCost, KoffeeSources.Find(consumer));
            if (payment == null) return null;

            double? previousStartUT = KerbalKoffeeScenario.Store.TryGetStart(
                kerbal.name, out double existingStartUT)
                ? existingStartUT : (double?)null;
            double untilUT = activationUT + KaffeinatedDuration;
            try
            {
                KerbalKoffeeScenario.Store.SetStart(kerbal.name, activationUT);
                return KaffeinationAttempt.Activated(
                    kerbal, previousStartUT, activationUT, untilUT, payment);
            }
            catch
            {
                payment.Dispose();
                throw;
            }
        }

        internal static string FormatDuration(double seconds)
        {
            seconds = Math.Max(0, seconds);
            int total = (int)Math.Floor(seconds);
            int hours = total / 3600;
            int minutes = total % 3600 / 60;
            int remainder = total % 60;
            return hours.ToString("00", CultureInfo.InvariantCulture) + ":" +
                minutes.ToString("00", CultureInfo.InvariantCulture) + ":" +
                remainder.ToString("00", CultureInfo.InvariantCulture);
        }
    }

    internal sealed class KaffeinationAttempt : IDisposable
    {
        private readonly ProtoCrewMember kerbal;
        private readonly double? previousStartUT;
        private readonly ResourcePayment? payment;
        private bool settled;

        internal bool NewlyActivated => payment != null;
        internal double StartUT { get; }
        internal double UntilUT { get; }

        private KaffeinationAttempt(ProtoCrewMember kerbal, double? previousStartUT,
            double startUT, double untilUT, ResourcePayment? payment)
        {
            this.kerbal = kerbal;
            this.previousStartUT = previousStartUT;
            this.payment = payment;
            StartUT = startUT;
            UntilUT = untilUT;
        }

        internal static KaffeinationAttempt AlreadyActive(ProtoCrewMember kerbal) =>
            new KaffeinationAttempt(kerbal, null, 0, 0, null);

        internal static KaffeinationAttempt Activated(ProtoCrewMember kerbal,
            double? previousStartUT, double startUT, double untilUT, ResourcePayment payment) =>
            new KaffeinationAttempt(kerbal, previousStartUT, startUT, untilUT, payment);

        internal void Commit()
        {
            if (settled) return;
            payment?.Commit();
            settled = true;
            if (payment != null)
            {
                Debug.Log("[Koffee] Consumed " + KoffeeSettingsLoader.Current.ActivationCost +
                    " whole cup(s) for " + kerbal.name + "; " + payment.Summary);
                Debug.Log("[Koffee] " + kerbal.name + " Kaffeinated until UT " +
                    UntilUT.ToString("R", CultureInfo.InvariantCulture));
            }
        }

        public void Dispose()
        {
            if (settled) return;
            try
            {
                if (payment != null)
                    KerbalKoffeeScenario.Store.RestoreStart(
                        kerbal.name, previousStartUT);
            }
            finally
            {
                payment?.Dispose();
                settled = true;
            }
        }
    }
}
