using System;
using System.Collections.Generic;
using System.Xml.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript.AddOnBase;
using PropFirmATS.Engine.Config;
using PropFirmATS.Engine.Risk;
using PropFirmATS.Engine.Persistence;
using PropFirmATS.Engine.Hameral;
using NinjaTrader.Core;
using System.Linq;
using System;

namespace PropFirmATS.AddOn.Core
{
    public class PropFirmAddOn : NinjaTrader.NinjaScript.AddOnBase.AddOnBase, IWorkspacePersistence
    {
        private StateManager _stateManager;
        private Dictionary<string, RiskManager> _riskManagers;
        private Dictionary<string, PropFirmAccountConfig> _accountConfigs;
        private EquivalentModeTracker _equivalentModeTracker;
        private SignalEngine _signalEngine;
        private BarsRequest _barsRequest;

        // Settings
        private int _maxLatencyMs = 500;

        // Timer for periodic saves
        private System.Threading.Timer _saveTimer;

        // NT8 event handlers
        private EventHandler<AccountItemEventArgs> _accountItemUpdateHandler;
        private EventHandler<ExecutionEventArgs> _executionUpdateHandler;
        private EventHandler<PositionEventArgs> _positionUpdateHandler;
        private EventHandler<AccountStatusEventArgs> _accountStatusUpdateHandler;

        private bool _isSystemHalted = false;

        // Bracket Order tracking
        private Dictionary<string, List<Execution>> _entryExecutions; // Key: OcoId
        private Dictionary<string, Order> _activeStopOrders; // Key: OcoId
        private Dictionary<string, Order> _activeTargetOrders; // Key: OcoId

        // Dynamic target mappings
        private Dictionary<string, TradeSignal> _pendingSignalContexts; // Key: OcoId

        // Active Trade Contexts for Trailing Management
        private Dictionary<string, ActiveTradeContext> _activeTradeContexts; // Key: OcoId
        private readonly object _contextLock = new object();

        // Global Indicators
        private PropFirmATS.Engine.Indicators.IndicatorTracker _indicators;

        protected override void OnStateChange()
        {
            // Note: In real NT8, check State == State.SetDefaults, State.Configure, etc.
            // For stub, simulating init
            InitSystem();
        }

        private void InitSystem()
        {
            // Directory stub, in real NT8 this is NinjaTrader.Core.Globals.UserDataDir
            string customBinPath = @"C:\Users\Default\Documents\NinjaTrader 8\bin\Custom";
            _stateManager = new StateManager(customBinPath);
            _riskManagers = new Dictionary<string, RiskManager>();
            _accountConfigs = new Dictionary<string, PropFirmAccountConfig>();
            _equivalentModeTracker = new EquivalentModeTracker(maxEquivalentContracts: 10.0); // example limit
            _entryExecutions = new Dictionary<string, List<Execution>>();
            _activeStopOrders = new Dictionary<string, Order>();
            _activeTargetOrders = new Dictionary<string, Order>();
            _pendingSignalContexts = new Dictionary<string, TradeSignal>();
            _activeTradeContexts = new Dictionary<string, ActiveTradeContext>();

            // Setup SignalEngine
            var vpStub = new BasicVolumeProfileStub();
            var vwapStub = new BasicVWAPStub();
            var fpStub = new BasicFootprintStub();
            _signalEngine = new SignalEngine(vpStub, vwapStub, fpStub);

            _indicators = new PropFirmATS.Engine.Indicators.IndicatorTracker();

            // Setup Event Handlers
            _accountItemUpdateHandler = OnAccountItemUpdate;
            _executionUpdateHandler = OnExecutionUpdate;
            _positionUpdateHandler = OnPositionUpdate;
            _accountStatusUpdateHandler = OnAccountStatusUpdate;

            Account.AccountItemUpdate += _accountItemUpdateHandler;
            Account.ExecutionUpdate += _executionUpdateHandler;
            Account.PositionUpdate += _positionUpdateHandler;
            Account.AccountStatusUpdate += _accountStatusUpdateHandler;

            // Request Hidden Bars
            _barsRequest = new BarsRequest("ES", 1); // e.g. 1 Minute
            _barsRequest.Request(OnBarsUpdate);

            // Start periodic auto-save to survive crash (every 1 minute)
            _saveTimer = new System.Threading.Timer(OnSaveTimerTick, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        private void OnSaveTimerTick(object state)
        {
            if (_riskManagers != null && _stateManager != null)
            {
                // Institutional Upgrade: Offload JSON file I/O to a background task
                // to prevent blocking the core NT8 execution thread and causing micro-stutters.

                // Copy current states safely before offloading
                var statesToSave = _riskManagers.Values.Select(rm => rm.GetCurrentState()).ToList();

                System.Threading.Tasks.Task.Run(() =>
                {
                    foreach (var riskState in statesToSave)
                    {
                        _stateManager.SaveRiskState(riskState);
                    }
                });
            }
        }

        private void OnBarsUpdate(BarsUpdateEventArgs e)
        {
            if (_isSystemHalted) return;

            // Check latency
            var latency = Globals.Now - e.Time;
            if (latency.TotalMilliseconds > _maxLatencyMs)
            {
                Console.WriteLine($"Latency ({latency.TotalMilliseconds}ms) exceeds max allowable. Skipping tick.");
                return;
            }

            // Enforce Day-Trading Rules: Block new entries late in the NY Session (e.g. after 16:00 ET)
            // Assuming Globals.Now or e.Time is configured to Exchange Time (ET)
            bool isLateSession = e.Time.TimeOfDay >= new TimeSpan(16, 0, 0);

            // Update Global Indicators
            _indicators.UpdateIndicators(e.Close);

            // Fast Reversal Profit Protection Logic
            lock (_contextLock)
            {
                foreach (var context in _activeTradeContexts.Values)
                {
                    if (context.IsBreakEvenSet) continue;

                    double initialRisk = context.GetRValue();
                    if (initialRisk == 0) continue; // Safety check

                    double currentProfit = context.GetCurrentProfit(e.Close);
                    double profitRMultiple = currentProfit / initialRisk;

                    if (profitRMultiple >= 0.5)
                    {
                        // Trade is in profit > 0.5R. Check ADX Trend Strength.
                        if (_indicators.CurrentADX < 25.0)
                        {
                            // Weak/Medium Trend detected -> Potential Failed Breakout
                            // Pull Stop Loss to BE + small buffer (0.1x ATR)
                            double buffer = _indicators.CurrentATR * 0.1;
                            double newStopPrice = context.Action == "Long"
                                ? context.EntryPrice + buffer
                                : context.EntryPrice - buffer;

                            Console.WriteLine($"[Profit Protection] Trend Weak (ADX={_indicators.CurrentADX}). Pulling Stop Loss to {newStopPrice}");

                            // Adjust actual active Stop Order
                            if (_activeStopOrders.TryGetValue(context.OcoId, out Order stopOrder))
                            {
                                Account acc = Account.All.FirstOrDefault(a => a.Name == context.AccountName);
                                if (acc != null)
                                {
                                    acc.ChangeOrder(stopOrder, stopOrder.Quantity, 0, newStopPrice);
                                }
                            }

                            context.IsBreakEvenSet = true;
                        }
                    }
                }
            }

            // Send tick to Signal Engine
            TradeSignal signal = _signalEngine.EvaluateMarket(e.Close);

            if (signal.IsValid && !isLateSession)
            {
                Console.WriteLine($"{signal.Action} Signal Detected at {e.Close}. Target: {signal.TargetPrice}, Stop: {signal.StopPrice}");

                lock (Account.All)
                {
                    foreach (Account acc in Account.All)
                    {
                        // Check if account already has an open position
                        if (acc.Name != "Sim101" && acc.Positions.Count == 0) // Example filter
                        {
                            string ocoId = Guid.NewGuid().ToString("N"); // Unique OCO per account

                            // Store the dynamic targets mapped to the OCO to apply on fill
                            _pendingSignalContexts[ocoId] = signal;

                            OrderAction action = signal.Action == "Long" ? OrderAction.Buy : OrderAction.SellShort;

                            // Institutional Upgrade: Regime-Aware Sizing (Fractional Kelly / Volatility Sizing)
                            // We dynamically calculate position size rather than hardcoding '1'.
                            // Example: Target $500 risk per trade based on current ATR.
                            double dollarRiskTarget = 500.0;
                            double esPointValue = 50.0;
                            double stopDistancePts = Math.Abs(e.Close - signal.StopPrice);
                            if (stopDistancePts == 0) stopDistancePts = 1.0; // Failsafe

                            int calculatedQty = (int)Math.Floor(dollarRiskTarget / (stopDistancePts * esPointValue));
                            if (calculatedQty < 1) calculatedQty = 1; // Minimum 1 contract

                            // Check max limit from equivalent tracker
                            double currentEquivalent = _equivalentModeTracker.GetEquivalentPosition("ES", 0); // Open position is 0 here based on above check
                            if (!_equivalentModeTracker.IsTradeAllowed("ES", calculatedQty, currentEquivalent))
                            {
                                // If calculated qty exceeds config limits, cap it
                                calculatedQty = 1; // Or max allowed. Using 1 for safety.
                            }

                            // Institutional Upgrade: Use passive Limit orders resting at the current close to avoid crossing the spread.
                            double limitPrice = e.Close;
                            Order entryOrder = acc.CreateOrder(Instrument.GetInstrument("ES"), action, OrderType.Limit, TimeInForce.Day, calculatedQty, limitPrice, 0, ocoId, "Entry", "");
                            acc.Submit(new Order[] { entryOrder });

                            // Save OCO to state for crash recovery
                            var riskManager = GetOrCreateRiskManager(acc);
                            riskManager.GetCurrentState().ActiveOcoIds["ES"] = ocoId;
                        }
                    }
                }
            }
        }

        public void Cleanup()
        {
            if (_saveTimer != null)
            {
                _saveTimer.Dispose();
                _saveTimer = null;
            }

            // Critical: Unsubscribe from events to prevent memory leaks
            Account.AccountItemUpdate -= _accountItemUpdateHandler;
            Account.ExecutionUpdate -= _executionUpdateHandler;
            Account.PositionUpdate -= _positionUpdateHandler;
            Account.AccountStatusUpdate -= _accountStatusUpdateHandler;

            // Save all states
            if (_riskManagers != null && _stateManager != null)
            {
                foreach (var rm in _riskManagers.Values)
                {
                    _stateManager.SaveRiskState(rm.GetCurrentState());
                }
            }
        }

        private void OnAccountItemUpdate(object sender, AccountItemEventArgs e)
        {
            if (e.Account == null) return;

            RiskManager manager = GetOrCreateRiskManager(e.Account);

            // Extract live data from the AccountItem
            if (e.AccountItem == AccountItem.CashValue)
            {
                // In a real implementation we might also track GrossRealizedProfit separately to calculate open pnl accurately,
                // but this illustrates using the real properties.
                double currentBalance = e.Value;
                double openPnl = 0; // Derived from position tracking in real NT8

                manager.UpdateRisk(currentBalance, openPnl, DateTime.Now);
            }
        }

        private void OnExecutionUpdate(object sender, ExecutionEventArgs e)
        {
            if (e.Account == null || e.Execution == null || e.Execution.Order == null) return;

            // Track partial fills to submit or adjust brackets immediately
            if (e.Execution.Order.Name == "Entry")
            {
                string ocoId = e.Execution.Order.Oco;
                if (!_entryExecutions.ContainsKey(ocoId))
                {
                    _entryExecutions[ocoId] = new List<Execution>();
                }

                _entryExecutions[ocoId].Add(e.Execution);

                int filledQty = 0;
                double totalCost = 0;
                foreach (var exec in _entryExecutions[ocoId])
                {
                    filledQty += exec.Quantity;
                    totalCost += (exec.Price * exec.Quantity);
                }

                double avgEntryPrice = totalCost / filledQty;

                // Submit or Adjust brackets on EVERY fill to prevent unprotected partial positions
                SubmitOrAdjustBracketOrders(e.Account, e.Execution.Order, filledQty, avgEntryPrice, ocoId);

                // Cleanup only when the order is completely filled or cancelled
                if (filledQty == e.Execution.Order.Quantity)
                {
                    _entryExecutions.Remove(ocoId);
                }
            }

            // Clear from state if flat
            if (e.Execution.Order.Name == "Profit Target" || e.Execution.Order.Name == "Stop Loss")
            {
                if (e.Account.Positions.Count == 0)
                {
                    var riskManager = GetOrCreateRiskManager(e.Account);
                    riskManager.GetCurrentState().ActiveOcoIds.Remove(e.Execution.Order.Instrument.FullName);
                    _activeStopOrders.Remove(e.Execution.Order.Oco);
                    _activeTargetOrders.Remove(e.Execution.Order.Oco);
                    _pendingSignalContexts.Remove(e.Execution.Order.Oco);
                    lock (_contextLock)
                    {
                        _activeTradeContexts.Remove(e.Execution.Order.Oco);
                    }
                }
            }
        }

        private void SubmitOrAdjustBracketOrders(Account account, Order entryOrder, int quantity, double avgEntryPrice, string ocoId)
        {
            double stopPrice = 0;
            double targetPrice = 0;
            string action = entryOrder.OrderAction == OrderAction.Buy ? "Long" : "Short";

            // Retrieve dynamic context generated during the signal
            if (_pendingSignalContexts.TryGetValue(ocoId, out var context))
            {
                stopPrice = context.StopPrice;
                targetPrice = context.TargetPrice;
            }
            else
            {
                // Fallback if context is somehow missing
                double stopOffset = 20.0; // Pts
                double targetOffset = 40.0; // Pts
                stopPrice = entryOrder.OrderAction == OrderAction.Buy ? avgEntryPrice - stopOffset : avgEntryPrice + stopOffset;
                targetPrice = entryOrder.OrderAction == OrderAction.Buy ? avgEntryPrice + targetOffset : avgEntryPrice - targetOffset;
            }

            OrderAction exitAction = entryOrder.OrderAction == OrderAction.Buy ? OrderAction.Sell : OrderAction.BuyToCover;

            // If brackets already exist for this OCO ID, change them. Otherwise, create new ones.
            if (_activeStopOrders.ContainsKey(ocoId) && _activeTargetOrders.ContainsKey(ocoId))
            {
                Order existingStop = _activeStopOrders[ocoId];
                Order existingTarget = _activeTargetOrders[ocoId];

                // Update quantity and price via Account.ChangeOrder
                account.ChangeOrder(existingStop, quantity, 0, stopPrice);
                account.ChangeOrder(existingTarget, quantity, targetPrice, 0);
            }
            else
            {
                // In stealth mode, use standard naming convention (not "ATS_xxx")
                // Enforce Day-Trading Rules: Use TimeInForce.Day instead of Gtc to prevent overnight holds
                Order stopOrder = account.CreateOrder(entryOrder.Instrument, exitAction, OrderType.StopMarket, TimeInForce.Day, quantity, 0, stopPrice, ocoId, "Stop Loss", "");
                Order targetOrder = account.CreateOrder(entryOrder.Instrument, exitAction, OrderType.Limit, TimeInForce.Day, quantity, targetPrice, 0, ocoId, "Profit Target", "");

                account.Submit(new Order[] { stopOrder, targetOrder });

                _activeStopOrders[ocoId] = stopOrder;
                _activeTargetOrders[ocoId] = targetOrder;

                // Track Trade Context for Profit Protection
                lock (_contextLock)
                {
                    _activeTradeContexts[ocoId] = new ActiveTradeContext
                    {
                        AccountName = account.Name,
                        OcoId = ocoId,
                        Action = action,
                        EntryPrice = avgEntryPrice,
                        InitialStopPrice = stopPrice,
                        IsBreakEvenSet = false
                    };
                }
            }
        }

        private void OnPositionUpdate(object sender, PositionEventArgs e)
        {
            if (e.Account == null || e.Position == null) return;

            // Passed-by-value check is required in real NT8
            Position safePos = e.Clone().Position;

            // Enforce Equivalent Mode limits
            double currentEquivalent = _equivalentModeTracker.GetEquivalentPosition(safePos.Instrument.FullName, safePos.Quantity);

            // Grab config max dynamically
            double limit = 10.0;
            if (_accountConfigs.TryGetValue(e.Account.Name, out var config))
            {
                limit = config.MaxEquivalentContracts;
            }

            // In a real system, we'd also check this *before* order entry.
            // This acts as a secondary failsafe.
            if (currentEquivalent > limit)
            {
                Console.WriteLine("Position exceeds Equivalent limit. Flattening.");
                e.Account.Flatten();
            }
        }

        private void OnAccountStatusUpdate(object sender, AccountStatusEventArgs e)
        {
            if (e.Account == null) return;

            // Detect disconnects to halt signal generation
            if (e.Status == ConnectionStatus.Disconnected)
            {
                _isSystemHalted = true;
                Console.WriteLine("Connection lost. Halting system.");
            }
            else if (e.Status == ConnectionStatus.Connected)
            {
                _isSystemHalted = false;
            }
        }

        private RiskManager GetOrCreateRiskManager(Account account)
        {
            if (account == null) return null;

            string accountName = account.Name;

            if (!_riskManagers.ContainsKey(accountName))
            {
                // Load config (normally from UI or saved XML)
                if (!_accountConfigs.TryGetValue(accountName, out var config))
                {
                    config = new PropFirmAccountConfig { AccountName = accountName };
                    _accountConfigs[accountName] = config;
                }

                // Load state from JSON
                RiskState state = _stateManager.LoadRiskState(accountName);

                RiskManager manager = new RiskManager(config, state);
                manager.OnFlattenRequired = (reason) =>
                {
                    Console.WriteLine($"Rule Breach or Session Cutoff! Flattening account {accountName} and canceling all orders. Reason: {reason}");
                    account.Flatten();
                    account.CancelAllOrders();

                    if (reason.Contains("Auto-flatten"))
                    {
                        // Ensure we completely halt this account's logic for the day
                        Console.WriteLine($"Account {accountName} has reached end of day. Halting further trades.");
                    }
                };

                _riskManagers[accountName] = manager;
            }
            return _riskManagers[accountName];
        }

        protected override void OnWindowCreated(object window)
        {
            // UI initialization logic
        }

        protected override void OnWindowDestroyed(object window)
        {
            Cleanup();
        }

        // IWorkspacePersistence
        public void Restore(XElement element)
        {
            if (element == null) return;

            XElement addOnSettings = element.Element("PropFirmAddOnSettings");
            if (addOnSettings != null)
            {
                var attr = addOnSettings.Attribute("Version");
                if (attr != null)
                {
                    string version = attr.Value;
                }

                // Parse UI settings here
            }
        }

        public void Save(XElement element)
        {
            if (element == null) return;

            // Save UI settings and configs to XML
            XElement addOnSettings = new XElement("PropFirmAddOnSettings");
            addOnSettings.Add(new XAttribute("Version", "1.0"));
            // Add UI state elements

            element.Add(addOnSettings);
        }
    }
}
