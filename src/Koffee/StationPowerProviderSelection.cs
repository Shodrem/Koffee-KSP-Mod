using System;

namespace ShoAerospace.Koffee
{
    internal enum StationPowerProviderKind { Stock, Brp }

    // One instance represents one KSP process. Loading another save must not
    // create or reset it. The integration will initialize it at MainMenu after
    // checking BRP and attaching all required callbacks successfully.
    internal sealed class StationPowerProviderSelection
    {
        private StationPowerProviderKind provider;
        private bool initializing;
        internal bool Initialized { get; private set; }
        internal string Failure { get; private set; } = "";
        internal StationPowerProviderKind Provider => Initialized ? provider :
            throw new InvalidOperationException("Electrical provider has not been selected at startup.");

        internal StationPowerProviderKind Initialize(Func<bool> initializeBrp)
        {
            if (Initialized) return provider;
            if (initializeBrp == null) throw new ArgumentNullException(nameof(initializeBrp));
            if (initializing) throw new InvalidOperationException("Electrical provider initialization is already in progress.");
            initializing = true;
            provider = StationPowerProviderKind.Stock;
            try
            {
                if (initializeBrp()) provider = StationPowerProviderKind.Brp;
            }
            catch (Exception error) { Failure = error.ToString(); }
            finally { Initialized = true; initializing = false; }
            return provider;
        }
    }
}
