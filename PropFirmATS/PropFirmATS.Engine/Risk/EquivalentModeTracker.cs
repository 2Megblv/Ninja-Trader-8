using System.Collections.Generic;

namespace PropFirmATS.Engine.Risk
{
    public class EquivalentModeTracker
    {
        // For demonstration, map of base instrument to multiplier
        // e.g., ES -> 1, MES -> 0.1 (so 10 MES = 1 ES)
        private Dictionary<string, double> _equivalentRatios = new Dictionary<string, double>
        {
            { "ES", 1.0 },
            { "MES", 0.1 },
            { "NQ", 1.0 },
            { "MNQ", 0.1 }
        };

        private double _maxEquivalentContracts;

        public EquivalentModeTracker(double maxEquivalentContracts)
        {
            _maxEquivalentContracts = maxEquivalentContracts;
        }

        public bool IsTradeAllowed(string instrumentName, int requestedContracts, double currentEquivalentPosition)
        {
            double ratio = GetRatio(instrumentName);
            double requestedEquivalent = requestedContracts * ratio;

            return (currentEquivalentPosition + requestedEquivalent) <= _maxEquivalentContracts;
        }

        public double GetEquivalentPosition(string instrumentName, int contracts)
        {
            return contracts * GetRatio(instrumentName);
        }

        private double GetRatio(string instrumentName)
        {
            // Simple mapping for stubs
            foreach (var kvp in _equivalentRatios)
            {
                if (instrumentName.Contains(kvp.Key))
                    return kvp.Value;
            }
            return 1.0; // Default 1:1 if not found
        }
    }
}
