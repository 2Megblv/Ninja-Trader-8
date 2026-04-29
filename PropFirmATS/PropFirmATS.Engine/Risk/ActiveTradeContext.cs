namespace PropFirmATS.Engine.Risk
{
    public class ActiveTradeContext
    {
        public string AccountName { get; set; }
        public string OcoId { get; set; }
        public string Action { get; set; } // "Long" or "Short"
        public double EntryPrice { get; set; }
        public double InitialStopPrice { get; set; }
        public double TargetPrice { get; set; }
        public bool IsBreakEvenSet { get; set; }
        public bool IsRolloverFlattenTriggered { get; set; }

        public double HighestRecordedProfit { get; set; }
        public bool IsHftFlattenTriggered { get; set; }

        public double GetRValue()
        {
            return System.Math.Abs(EntryPrice - InitialStopPrice);
        }

        public double GetCurrentProfit(double currentPrice)
        {
            if (Action == "Long")
            {
                return currentPrice - EntryPrice;
            }
            else
            {
                return EntryPrice - currentPrice;
            }
        }
    }
}
