using System;

namespace PropFirmATS.Engine.Config
{
    public enum DrawdownType
    {
        TrailingIntraday,
        TrailingEOD
    }

    public class PropFirmAccountConfig
    {
        public string AccountName { get; set; } = string.Empty;

        // Settings
        public double PropStartingBalance { get; set; } = 50000.0;
        public double MaxDrawdown { get; set; } = 2500.0;
        public double DailyLossLimit { get; set; } = 1250.0;
        public double ConsistencyProfitCap { get; set; } = 1500.0;
        public DrawdownType DrawdownType { get; set; } = DrawdownType.TrailingIntraday;
        public bool IsApexLockEnabled { get; set; } = true;
        public double MaxEquivalentContracts { get; set; } = 10.0;

        // Auto-Flatten
        public TimeSpan AutoFlattenTime { get; set; } = new TimeSpan(16, 45, 0); // 4:45 PM
    }
}
