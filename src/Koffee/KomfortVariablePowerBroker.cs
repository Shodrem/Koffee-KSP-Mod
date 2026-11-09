using System;

namespace ShoAerospace.Koffee
{
    // Stock still chooses TimeFactor and processes the recipe once. Only the EC
    // coordinate is mapped through its exact cost integral; other resources keep
    // their native availability, priorities, flow locks and output semantics.
    internal sealed class KomfortVariablePowerBroker : IResourceBroker
    {
        private readonly IResourceBroker inner;
        private readonly double rate, maximum;
        private readonly Func<double, double> cost;
        private readonly int ec;
        internal double Accepted { get; private set; } = double.PositiveInfinity;
        internal KomfortVariablePowerBroker(IResourceBroker inner, double rate, double maximum, Func<double, double> cost)
        { this.inner = inner; this.rate = rate; this.maximum = maximum; this.cost = cost; ec = PartResourceLibrary.Instance.GetDefinition("ElectricCharge").id; }
        private double Virtual(double amount, double limit) => rate * KomfortRate.Limit(0, limit, Math.Max(0, amount), (_, b) => cost(b), conservative: true);
        private double Request(double nominal, Func<double, double> request)
        {
            if (nominal <= 0) return request(nominal);
            double work = Math.Min(maximum, nominal / rate);
            double received = Math.Max(0, request(cost(work)));
            double equivalent = Virtual(received, work);
            Accepted = Math.Min(Accepted, equivalent / rate);
            return equivalent;
        }
        public double AmountAvailable(Part part, string name, double dt, ResourceFlowMode mode)
        { double value = inner.AmountAvailable(part, name, dt, mode); return name == "ElectricCharge" ? Virtual(value, maximum) : value; }
        public double AmountAvailable(Part part, int id, double dt, ResourceFlowMode mode)
        { double value = inner.AmountAvailable(part, id, dt, mode); return id == ec ? Virtual(value, maximum) : value; }
        public double RequestResource(Part part, string name, double amount, double dt, ResourceFlowMode mode) => name == "ElectricCharge" ?
            Request(amount, quantity => inner.RequestResource(part, name, quantity, dt, mode)) : inner.RequestResource(part, name, amount, dt, mode);
        public double RequestResource(Part part, int id, double amount, double dt, ResourceFlowMode mode) => id == ec ?
            Request(amount, quantity => inner.RequestResource(part, id, quantity, dt, mode)) : inner.RequestResource(part, id, amount, dt, mode);
        public double StorageAvailable(Part part, string name, double dt, ResourceFlowMode mode, double fill) => inner.StorageAvailable(part, name, dt, mode, fill);
        public double StorageAvailable(Part part, int id, double dt, ResourceFlowMode mode, double fill) => inner.StorageAvailable(part, id, dt, mode, fill);
        public double StoreResource(Part part, string name, double amount, double dt, ResourceFlowMode mode) => inner.StoreResource(part, name, amount, dt, mode);
        public double StoreResource(Part part, int id, double amount, double dt, ResourceFlowMode mode) => inner.StoreResource(part, id, amount, dt, mode);
    }
}
