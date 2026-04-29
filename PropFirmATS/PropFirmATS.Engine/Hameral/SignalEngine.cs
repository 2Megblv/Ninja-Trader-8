namespace PropFirmATS.Engine.Hameral
{
    // Stub for Hameral Basic Volume Profile
    public class BasicVolumeProfileStub
    {
        public double VAH { get; set; }
        public double VAL { get; set; }
        public double POC { get; set; }

        public void UpdateData(double vah, double val, double poc)
        {
            VAH = vah;
            VAL = val;
            POC = poc;
        }
    }

    // Stub for Hameral Basic VWAP
    public class BasicVWAPStub
    {
        public double VWAP { get; set; }
        public double StdDev1Upper { get; set; }
        public double StdDev1Lower { get; set; }
        public double StdDev2Upper { get; set; }
        public double StdDev2Lower { get; set; }

        public bool IsUpwardSloping { get; set; }

        public void UpdateData(double vwap, double std1U, double std1L, double std2U, double std2L, bool slope)
        {
            VWAP = vwap;
            StdDev1Upper = std1U;
            StdDev1Lower = std1L;
            StdDev2Upper = std2U;
            StdDev2Lower = std2L;
            IsUpwardSloping = slope;
        }
    }

    // Stub for Hameral Footprint / Delta
    public class BasicFootprintStub
    {
        public double BidVolume { get; set; }
        public double AskVolume { get; set; }
        public double CandleDelta { get; set; }
        public bool IsCandleBullish { get; set; }

        public void UpdateData(double bidVol, double askVol, double delta, bool isBullish)
        {
            BidVolume = bidVol;
            AskVolume = askVol;
            CandleDelta = delta;
            IsCandleBullish = isBullish;
        }
    }

    public enum MarketMode
    {
        Trend,
        Range,
        Unknown
    }

    public class TradeSignal
    {
        public bool IsValid { get; set; }
        public string Action { get; set; } // "Long" or "Short"
        public double TargetPrice { get; set; }
        public double StopPrice { get; set; }
    }

    public class SignalEngine
    {
        private BasicVolumeProfileStub _volProfile;
        private BasicVWAPStub _vwap;
        private BasicFootprintStub _footprint;

        // Relative volume tracker stub
        private double _averageVolume = 300.0;

        // Multi-Timeframe (MTF) Trend Bias Stubs (e.g. 5M, 1H, 4H)
        // Simulated tracking of higher timeframe SMA/VWAP slopes. > 0 is Bullish, < 0 is Bearish.
        private double _trendBias5M = 1.0;
        private double _trendBias1H = 1.0;
        private double _trendBias4H = 1.0;

        // Simulating slope calculations for MTF context
        // In a real environment, this would compare the higher timeframe's close to its own moving average or VWAP.
        // Here we simulate the trend state (e.g. comparing close price to a simulated long-term SMA).
        public void UpdateTrendBias5M(double closePrice) { _trendBias5M = closePrice > (_vwap.VWAP + 5) ? 1.0 : -1.0; }
        public void UpdateTrendBias1H(double closePrice) { _trendBias1H = closePrice > (_vwap.VWAP + 20) ? 1.0 : -1.0; }
        public void UpdateTrendBias4H(double closePrice) { _trendBias4H = closePrice > (_vwap.VWAP + 50) ? 1.0 : -1.0; }

        public SignalEngine(BasicVolumeProfileStub vp, BasicVWAPStub vwap, BasicFootprintStub fp)
        {
            _volProfile = vp;
            _vwap = vwap;
            _footprint = fp;
        }

        public MarketMode DetermineMarketMode(double currentPrice)
        {
            if (currentPrice > _vwap.StdDev1Upper && _vwap.IsUpwardSloping)
            {
                return MarketMode.Trend;
            }
            if (currentPrice < _vwap.StdDev1Upper && currentPrice > _vwap.StdDev1Lower)
            {
                return MarketMode.Range;
            }
            return MarketMode.Unknown;
        }

        public TradeSignal EvaluateMarket(double currentPrice)
        {
            TradeSignal signal = new TradeSignal { IsValid = false };
            MarketMode mode = DetermineMarketMode(currentPrice);

            bool inZone = false;
            double zoneThreshold = 2.0;

            double calculatedTarget = 0;
            double calculatedStop = 0;

            if (mode == MarketMode.Trend)
            {
                // Pullback to +1 StdDev or VWAP
                if (System.Math.Abs(currentPrice - _vwap.StdDev1Upper) < zoneThreshold ||
                    System.Math.Abs(currentPrice - _vwap.VWAP) < zoneThreshold)
                {
                    inZone = true;
                    // Dynamic Targets: Target is the recent high or +2 StdDev, Stop is structurally below VWAP
                    calculatedTarget = _vwap.StdDev2Upper;
                    calculatedStop = _vwap.VWAP - 2.0; // 2 pts below VWAP
                }
            }
            else if (mode == MarketMode.Range)
            {
                // Fade at VAL or -1 StdDev
                if (System.Math.Abs(currentPrice - _volProfile.VAL) < zoneThreshold ||
                    System.Math.Abs(currentPrice - _vwap.StdDev1Lower) < zoneThreshold)
                {
                    inZone = true;
                    // Dynamic Targets: Mean reversion back to VWAP or POC. Stop below -2 StdDev.
                    calculatedTarget = _vwap.VWAP;
                    calculatedStop = _vwap.StdDev2Lower - 1.0;
                }
            }

            if (!inZone) return signal;

            // Update average volume dynamically in a real system. Here we use our stub property.
            double currentTotalVol = _footprint.AskVolume + _footprint.BidVolume;

            // 1 & 2. Absorption & Imbalance Detection
            bool isBullishImbalance = _footprint.AskVolume >= (_footprint.BidVolume * 3);
            bool isBearishImbalance = _footprint.BidVolume >= (_footprint.AskVolume * 3);

            // Institutional Update: Use relative volume instead of hardcoded 1000
            bool isHighRelativeVolume = currentTotalVol > (_averageVolume * 2);
            bool isBullishAbsorption = isHighRelativeVolume && (_footprint.CandleDelta > 0);
            bool isBearishAbsorption = isHighRelativeVolume && (_footprint.CandleDelta < 0);

            // 3. Candle Close & Delta
            bool bullishDeltaConfirmed = _footprint.CandleDelta > 0 && _footprint.IsCandleBullish;
            bool bearishDeltaConfirmed = _footprint.CandleDelta < 0 && !_footprint.IsCandleBullish;

            // 4. MTF Trend Alignment (Directional Filter)
            // We use 1H/4H strictly to confirm the broad directional bias.
            bool isMtfBullishAligned = _trendBias1H > 0 && _trendBias4H > 0;
            bool isMtfBearishAligned = _trendBias1H < 0 && _trendBias4H < 0;

            // The 5M is used for immediate momentum confirmation
            bool isImmediateMomentumBullish = _trendBias5M > 0;
            bool isImmediateMomentumBearish = _trendBias5M < 0;

            // Long Evaluation
            if ((isBullishImbalance || isBullishAbsorption) && bullishDeltaConfirmed && isMtfBullishAligned && isImmediateMomentumBullish)
            {
                signal.IsValid = true;
                signal.Action = "Long";
                signal.TargetPrice = calculatedTarget;
                signal.StopPrice = calculatedStop;
            }
            // Short Evaluation
            else if ((isBearishImbalance || isBearishAbsorption) && bearishDeltaConfirmed && isMtfBearishAligned && isImmediateMomentumBearish)
            {
                signal.IsValid = true;
                signal.Action = "Short";

                // Inverse targets for shorts (mock structural levels)
                signal.TargetPrice = _vwap.StdDev2Lower; // Example
                signal.StopPrice = _vwap.VWAP + 2.0;
            }

            return signal;
        }
    }
}
