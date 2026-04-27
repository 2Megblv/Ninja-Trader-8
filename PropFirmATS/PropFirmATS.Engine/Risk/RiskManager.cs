using System;
using PropFirmATS.Engine.Config;

namespace PropFirmATS.Engine.Risk
{
    public class RiskState
    {
        public string AccountName { get; set; } = string.Empty;

        // EOD tracking
        public double HighWaterMarkRealized { get; set; }

        // Intraday tracking
        public double HighWaterMarkUnrealized { get; set; }

        // Locked state
        public bool IsDrawdownFloorLocked { get; set; }
        public double LockedDrawdownFloor { get; set; }

        public double DailyRealizedPnl { get; set; }

        // Active Orders Persistence (InstrumentName -> OcoId)
        public System.Collections.Generic.Dictionary<string, string> ActiveOcoIds { get; set; } = new System.Collections.Generic.Dictionary<string, string>();
    }

    public class RiskManager
    {
        private PropFirmAccountConfig _config;
        private RiskState _state;

        // Action delegate to trigger account flatten, passes string reason
        public Action<string> OnFlattenRequired { get; set; }

        public RiskManager(PropFirmAccountConfig config, RiskState state)
        {
            _config = config;
            _state = state;

            if (_state.HighWaterMarkRealized == 0)
                _state.HighWaterMarkRealized = _config.PropStartingBalance;

            if (_state.HighWaterMarkUnrealized == 0)
                _state.HighWaterMarkUnrealized = _config.PropStartingBalance;
        }

        public void UpdateRisk(double currentBalance, double openPnl, DateTime currentTime)
        {
            double currentEquity = currentBalance + openPnl;

            // 1. Auto-Flatten Cutoff
            if (currentTime.TimeOfDay >= _config.AutoFlattenTime && currentTime.TimeOfDay < _config.AutoFlattenTime.Add(TimeSpan.FromMinutes(1)))
            {
                TriggerFlatten("Auto-flatten time reached.");
                return;
            }

            // 2. Daily Loss Limit
            if (_state.DailyRealizedPnl + openPnl <= -_config.DailyLossLimit)
            {
                TriggerFlatten("Daily Loss Limit breached.");
                return;
            }

            // 3. Consistency Profit Cap
            if (_state.DailyRealizedPnl + openPnl >= _config.ConsistencyProfitCap)
            {
                TriggerFlatten("Consistency Profit Cap reached.");
                return;
            }

            // 4. Update Drawdown Trackers
            if (_config.DrawdownType == DrawdownType.TrailingIntraday)
            {
                if (currentEquity > _state.HighWaterMarkUnrealized)
                {
                    _state.HighWaterMarkUnrealized = currentEquity;
                }
            }

            // 5. Apex Lock (Safety Net Lock)
            if (_config.IsApexLockEnabled && !_state.IsDrawdownFloorLocked)
            {
                double peakToEvaluate = _config.DrawdownType == DrawdownType.TrailingIntraday ? _state.HighWaterMarkUnrealized : _state.HighWaterMarkRealized;
                if (peakToEvaluate >= _config.PropStartingBalance + _config.MaxDrawdown + 100)
                {
                    _state.IsDrawdownFloorLocked = true;
                    _state.LockedDrawdownFloor = _config.PropStartingBalance + 100;
                }
            }

            // 6. Drawdown Floor Breach Check
            double currentFloor = GetCurrentDrawdownFloor();
            if (currentEquity <= currentFloor)
            {
                TriggerFlatten("Drawdown Floor breached.");
                return;
            }
        }

        public void OnSessionClose(double currentBalance)
        {
            // Reset daily PNL
            _state.DailyRealizedPnl = 0;

            if (_config.DrawdownType == DrawdownType.TrailingEOD)
            {
                if (currentBalance > _state.HighWaterMarkRealized)
                {
                    _state.HighWaterMarkRealized = currentBalance;
                }
            }
        }

        public void RegisterTradeRealized(double pnl)
        {
            _state.DailyRealizedPnl += pnl;
        }

        private double GetCurrentDrawdownFloor()
        {
            if (_state.IsDrawdownFloorLocked)
            {
                return _state.LockedDrawdownFloor;
            }

            double peak = _config.DrawdownType == DrawdownType.TrailingIntraday ? _state.HighWaterMarkUnrealized : _state.HighWaterMarkRealized;
            return peak - _config.MaxDrawdown;
        }

        private void TriggerFlatten(string reason)
        {
            OnFlattenRequired?.Invoke(reason);
        }

        public RiskState GetCurrentState()
        {
            return _state;
        }
    }
}
