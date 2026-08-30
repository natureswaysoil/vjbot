#region Using declarations
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// Combines the VJ2 Supertrend and UT Bot trailing-stop logic.
    /// Entries occur when both components first agree on direction.
    /// This strategy is never enabled automatically.
    /// </summary>
    public class VJ2UTBotLossFirstXTrendStrategy : Strategy
    {
        private ATR supertrendAtr;
        private ATR utAtr;
        private ATR riskAtr;

        private double trendUp;
        private double trendDown;
        private double previousTrendUp;
        private double previousTrendDown;
        private int supertrendDirection;

        private double utTrailingStop;
        private double previousUtTrailingStop;
        private double previousSource;

        private bool previousLongAgreement;
        private bool previousShortAgreement;
        private bool initialized;

        // Loss-first controls
        private int pendingDirection;
        private int pendingSignalBar = -1;
        private Queue<int> agreementHistory = new Queue<int>();
        private int lastProcessedTradeCount;
        private int cooldownUntilBar = -1;
        private DateTime currentTradingDate = Core.Globals.MinDate;
        private double dayStartCumProfit;
        private double entryPrice;
        private double highestSinceEntry;
        private double lowestSinceEntry;
        private bool breakevenActivated;

        private WaveTrendState[] wtStates;

        private class WaveTrendState
        {
            public bool Initialized;
            public double Esa;
            public double Dev;
            public double Wt1;
            public double Wt2;
            public Queue<double> History = new Queue<double>();
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "VJ2 + UT Bot loss-first strategy with 5m/15m/30m/1h WaveTrend X-Trend filter.";
                Name = "VJ2 UT Bot Loss First XTrend Strategy";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsFillLimitOnTouch = false;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                Slippage = 0;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Gtc;
                TraceOrders = false;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 20;
                IsInstantiatedOnEachOptimizationIteration = true;
                IncludeCommission = true;
                DefaultQuantity = 1;

                SupertrendFactor = 3.0;
                SupertrendAtrPeriod = 14;
                UtKeyValue = 1.0;
                UtAtrPeriod = 10;
                UseHeikinAshiSource = false;

                EnableLongs = true;
                EnableShorts = true;
                CloseOnDisagreement = true;

                UseAtrRiskExits = true;
                RiskAtrPeriod = 14;
                StopAtrMultiple = 1.0;
                TargetAtrMultiple = 3.0;

                MaxStopPoints = 15.0;
                RequireOneBarConfirmation = true;
                UseChopFilter = true;
                ChopLookbackBars = 4;
                MaxDirectionFlips = 1;
                CooldownBarsAfterLoss = 3;
                MaxDailyRealizedLoss = 175.0;
                UseBreakeven = true;
                BreakevenTriggerPoints = 10.0;
                BreakevenPlusPoints = 1.0;
                UseProfitTrail = true;
                TrailTriggerPoints = 20.0;
                TrailDistancePoints = 10.0;

                UseXTrendFilter = true;
                WaveTrendChannelLength = 10;
                WaveTrendAverageLength = 21;
                MinimumAlignedTimeframes = 3;
                MinimumIntradayTrendScore = 3.0;

                ShowSignalMarkers = true;
                ShowTrailingLines = true;
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Minute, 15);
                AddDataSeries(BarsPeriodType.Minute, 30);
                AddDataSeries(BarsPeriodType.Minute, 60);
            }
            else if (State == State.DataLoaded)
            {
                supertrendAtr = ATR(SupertrendAtrPeriod);
                utAtr = ATR(UtAtrPeriod);
                riskAtr = ATR(RiskAtrPeriod);
                wtStates = new WaveTrendState[4];
                for (int i = 0; i < 4; i++) wtStates[i] = new WaveTrendState();
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress >= 0 && BarsInProgress <= 3)
                UpdateWaveTrend(BarsInProgress);

            if (BarsInProgress != 0)
                return;

            int requiredBars = Math.Max(Math.Max(SupertrendAtrPeriod, UtAtrPeriod), RiskAtrPeriod) + 2;
            if (CurrentBar < requiredBars)
                return;

            if (UseXTrendFilter && (CurrentBars[1] < WaveTrendAverageLength + 5 || CurrentBars[2] < WaveTrendAverageLength + 5 || CurrentBars[3] < WaveTrendAverageLength + 5))
                return;

            UpdateDailyState();
            ProcessClosedTrades();

            double medianPrice = (High[0] + Low[0]) / 2.0;
            double up = medianPrice - SupertrendFactor * supertrendAtr[0];
            double down = medianPrice + SupertrendFactor * supertrendAtr[0];

            // Initialize the recursive values on the first eligible bar.
            if (!initialized)
            {
                trendUp = up;
                trendDown = down;
                previousTrendUp = up;
                previousTrendDown = down;
                supertrendDirection = 1;

                previousSource = GetSource(1);
                previousUtTrailingStop = 0.0;
                utTrailingStop = 0.0;
                previousLongAgreement = false;
                previousShortAgreement = false;
                initialized = true;
            }

            previousTrendUp = trendUp;
            previousTrendDown = trendDown;

            trendUp = Close[1] > previousTrendUp ? Math.Max(up, previousTrendUp) : up;
            trendDown = Close[1] < previousTrendDown ? Math.Min(down, previousTrendDown) : down;

            if (Close[0] > previousTrendDown)
                supertrendDirection = 1;
            else if (Close[0] < previousTrendUp)
                supertrendDirection = -1;

            double source = GetSource(0);
            double utLoss = UtKeyValue * utAtr[0];
            previousUtTrailingStop = utTrailingStop;

            if (source > previousUtTrailingStop && previousSource > previousUtTrailingStop)
                utTrailingStop = Math.Max(previousUtTrailingStop, source - utLoss);
            else if (source < previousUtTrailingStop && previousSource < previousUtTrailingStop)
                utTrailingStop = Math.Min(previousUtTrailingStop, source + utLoss);
            else
                utTrailingStop = source > previousUtTrailingStop ? source - utLoss : source + utLoss;

            bool utBull = source > utTrailingStop;
            bool utBear = source < utTrailingStop;
            bool longAgreement = supertrendDirection == 1 && utBull;
            bool shortAgreement = supertrendDirection == -1 && utBear;
            bool longSignal = longAgreement && !previousLongAgreement;
            bool shortSignal = shortAgreement && !previousShortAgreement;

            if (ShowTrailingLines)
            {
                Brush supertrendBrush = supertrendDirection == 1 ? Brushes.LimeGreen : Brushes.Red;
                Brush utBrush = utBull ? Brushes.Teal : Brushes.DarkOrange;
                double supertrendLine = supertrendDirection == 1 ? trendUp : trendDown;
                double previousSupertrendLine = supertrendDirection == 1 ? previousTrendUp : previousTrendDown;

                Draw.Line(this, "VJ2Line" + CurrentBar, false, 1, previousSupertrendLine, 0, supertrendLine, supertrendBrush, DashStyleHelper.Solid, 2);
                Draw.Line(this, "UTLine" + CurrentBar, false, 1, previousUtTrailingStop, 0, utTrailingStop, utBrush, DashStyleHelper.Solid, 2);
            }

            int agreementDirection = longAgreement ? 1 : (shortAgreement ? -1 : 0);
            UpdateAgreementHistory(agreementDirection);

            ManageOpenPosition(longAgreement, shortAgreement);

            bool dailyLocked = GetTodayRealizedPnL() <= -MaxDailyRealizedLoss;
            bool coolingDown = CurrentBar <= cooldownUntilBar;
            bool choppy = UseChopFilter && CountDirectionFlips() > MaxDirectionFlips;
            bool xTrendLongAllowed = !UseXTrendFilter || XTrendAllowsDirection(1);
            bool xTrendShortAllowed = !UseXTrendFilter || XTrendAllowsDirection(-1);

            bool canEnter = Position.MarketPosition == MarketPosition.Flat
                            && !dailyLocked
                            && !coolingDown
                            && !choppy;

            if (RequireOneBarConfirmation)
            {
                if (longSignal && EnableLongs && xTrendLongAllowed)
                {
                    pendingDirection = 1;
                    pendingSignalBar = CurrentBar;
                }
                else if (shortSignal && EnableShorts && xTrendShortAllowed)
                {
                    pendingDirection = -1;
                    pendingSignalBar = CurrentBar;
                }

                if (canEnter && pendingSignalBar >= 0 && CurrentBar == pendingSignalBar + 1)
                {
                    if (pendingDirection == 1 && longAgreement && EnableLongs && XTrendAllowsDirection(1))
                        EnterLossControlledTrade(1);
                    else if (pendingDirection == -1 && shortAgreement && EnableShorts && XTrendAllowsDirection(-1))
                        EnterLossControlledTrade(-1);

                    pendingDirection = 0;
                    pendingSignalBar = -1;
                }
                else if (pendingSignalBar >= 0 && CurrentBar > pendingSignalBar + 1)
                {
                    pendingDirection = 0;
                    pendingSignalBar = -1;
                }
            }
            else if (canEnter)
            {
                if (longSignal && EnableLongs && xTrendLongAllowed)
                    EnterLossControlledTrade(1);
                else if (shortSignal && EnableShorts && xTrendShortAllowed)
                    EnterLossControlledTrade(-1);
            }

            previousSource = source;
            previousLongAgreement = longAgreement;
            previousShortAgreement = shortAgreement;
        }

        private double GetSource(int barsAgo)
        {
            // TradingView's Heikin-Ashi close is (O + H + L + C) / 4.
            if (UseHeikinAshiSource)
                return (Open[barsAgo] + High[barsAgo] + Low[barsAgo] + Close[barsAgo]) / 4.0;

            return Close[barsAgo];
        }

        private void UpdateWaveTrend(int bip)
        {
            if (CurrentBars[bip] < 1) return;
            WaveTrendState st = wtStates[bip];
            double ap = (Highs[bip][0] + Lows[bip][0] + Closes[bip][0]) / 3.0;
            double a1 = 2.0 / (WaveTrendChannelLength + 1.0);
            double a2 = 2.0 / (WaveTrendAverageLength + 1.0);

            if (!st.Initialized)
            {
                st.Esa = ap; st.Dev = 0.0; st.Wt1 = 0.0; st.Wt2 = 0.0;
                st.History.Enqueue(0.0); st.Initialized = true; return;
            }

            st.Esa = a1 * ap + (1.0 - a1) * st.Esa;
            st.Dev = a1 * Math.Abs(ap - st.Esa) + (1.0 - a1) * st.Dev;
            double ci = st.Dev <= 1e-12 ? 0.0 : (ap - st.Esa) / (0.015 * st.Dev);
            st.Wt1 = a2 * ci + (1.0 - a2) * st.Wt1;
            st.History.Enqueue(st.Wt1);
            while (st.History.Count > 4) st.History.Dequeue();
            double sum = 0.0; foreach (double v in st.History) sum += v;
            st.Wt2 = sum / st.History.Count;
        }

        private bool XTrendAllowsDirection(int direction)
        {
            if (!UseXTrendFilter) return true;
            int aligned = 0;
            double score = 0.0;
            double[] weights = { 1.0, 2.0, 3.0, 4.0 };

            for (int i = 0; i < 4; i++)
            {
                WaveTrendState st = wtStates[i];
                if (!st.Initialized) return false;
                int d = st.Wt1 > st.Wt2 ? 1 : (st.Wt1 < st.Wt2 ? -1 : 0);
                if (d == direction) aligned++;
                score += GetWaveTrendContribution(st.Wt1, d, weights[i]);
            }

            return direction > 0
                ? aligned >= MinimumAlignedTimeframes && score >= MinimumIntradayTrendScore
                : aligned >= MinimumAlignedTimeframes && score <= -MinimumIntradayTrendScore;
        }

        private double GetWaveTrendContribution(double wt1, int direction, double maxWeight)
        {
            if (direction == 0) return 0.0;
            double f;
            if (direction > 0)
            {
                f = wt1 > 25 ? 0.25 : wt1 > 0 ? 0.50 : wt1 > -25 ? 0.75 : 1.0;
                return maxWeight * f;
            }
            f = wt1 < -25 ? 0.25 : wt1 < 0 ? 0.50 : wt1 < 25 ? 0.75 : 1.0;
            return -maxWeight * f;
        }

        private void EnterLossControlledTrade(int direction)
        {
            int atrStopTicks = Math.Max(1, (int)Math.Round(StopAtrMultiple * riskAtr[0] / TickSize));
            int hardStopTicks = Math.Max(1, (int)Math.Round(MaxStopPoints / TickSize));
            int stopTicks = Math.Min(atrStopTicks, hardStopTicks);

            if (direction == 1)
            {
                SetStopLoss("Long", CalculationMode.Ticks, stopTicks, false);
                EnterLong(DefaultQuantity, "Long");

                if (ShowSignalMarkers)
                {
                    Draw.ArrowUp(this, "LongArrow" + CurrentBar, true, 0, Low[0] - 2 * TickSize, Brushes.Lime);
                    Draw.Text(this, "LongText" + CurrentBar, "LONG", 0, Low[0] - 4 * TickSize, Brushes.Lime);
                }
            }
            else
            {
                SetStopLoss("Short", CalculationMode.Ticks, stopTicks, false);
                EnterShort(DefaultQuantity, "Short");

                if (ShowSignalMarkers)
                {
                    Draw.ArrowDown(this, "ShortArrow" + CurrentBar, true, 0, High[0] + 2 * TickSize, Brushes.Red);
                    Draw.Text(this, "ShortText" + CurrentBar, "SHORT", 0, High[0] + 4 * TickSize, Brushes.Red);
                }
            }

            entryPrice = Close[0];
            highestSinceEntry = Close[0];
            lowestSinceEntry = Close[0];
            breakevenActivated = false;
        }

        private void ManageOpenPosition(bool longAgreement, bool shortAgreement)
        {
            if (Position.MarketPosition == MarketPosition.Flat)
                return;

            if (Position.AveragePrice > 0)
                entryPrice = Position.AveragePrice;

            highestSinceEntry = Math.Max(highestSinceEntry, High[0]);
            lowestSinceEntry = Math.Min(lowestSinceEntry, Low[0]);

            if (Position.MarketPosition == MarketPosition.Long)
            {
                double favorable = highestSinceEntry - entryPrice;

                if (UseBreakeven && favorable >= BreakevenTriggerPoints)
                {
                    SetStopLoss("Long", CalculationMode.Price, entryPrice + BreakevenPlusPoints, false);
                    breakevenActivated = true;
                }

                if (UseProfitTrail && favorable >= TrailTriggerPoints)
                {
                    double trail = highestSinceEntry - TrailDistancePoints;
                    if (breakevenActivated)
                        trail = Math.Max(trail, entryPrice + BreakevenPlusPoints);

                    SetStopLoss("Long", CalculationMode.Price, trail, false);
                }

                if (CloseOnDisagreement && !longAgreement)
                    ExitLong("Agreement Lost", "Long");
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                double favorable = entryPrice - lowestSinceEntry;

                if (UseBreakeven && favorable >= BreakevenTriggerPoints)
                {
                    SetStopLoss("Short", CalculationMode.Price, entryPrice - BreakevenPlusPoints, false);
                    breakevenActivated = true;
                }

                if (UseProfitTrail && favorable >= TrailTriggerPoints)
                {
                    double trail = lowestSinceEntry + TrailDistancePoints;
                    if (breakevenActivated)
                        trail = Math.Min(trail, entryPrice - BreakevenPlusPoints);

                    SetStopLoss("Short", CalculationMode.Price, trail, false);
                }

                if (CloseOnDisagreement && !shortAgreement)
                    ExitShort("Agreement Lost", "Short");
            }
        }

        private void UpdateAgreementHistory(int direction)
        {
            agreementHistory.Enqueue(direction);
            while (agreementHistory.Count > ChopLookbackBars)
                agreementHistory.Dequeue();
        }

        private int CountDirectionFlips()
        {
            int flips = 0;
            int previousNonZero = 0;

            foreach (int dir in agreementHistory)
            {
                if (dir == 0)
                    continue;

                if (previousNonZero != 0 && dir != previousNonZero)
                    flips++;

                previousNonZero = dir;
            }

            return flips;
        }

        private void ProcessClosedTrades()
        {
            int tradeCount = SystemPerformance.AllTrades.Count;

            if (tradeCount <= lastProcessedTradeCount)
                return;

            for (int i = lastProcessedTradeCount; i < tradeCount; i++)
            {
                Trade trade = SystemPerformance.AllTrades[i];
                if (trade.ProfitCurrency < 0)
                    cooldownUntilBar = CurrentBar + CooldownBarsAfterLoss;
            }

            lastProcessedTradeCount = tradeCount;
        }

        private void UpdateDailyState()
        {
            DateTime barDate = Time[0].Date;

            if (currentTradingDate != barDate)
            {
                currentTradingDate = barDate;
                dayStartCumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
            }
        }

        private double GetTodayRealizedPnL()
        {
            return SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - dayStartCumProfit;
        }

        #region Properties
        [NinjaScriptProperty]
        [Range(1.0, 140.0)]
        [Display(Name = "Factor", GroupName = "VJ2 Supertrend", Order = 0)]
        public double SupertrendFactor { get; set; }

        [NinjaScriptProperty]
        [Range(1, 140)]
        [Display(Name = "ATR Period", GroupName = "VJ2 Supertrend", Order = 1)]
        public int SupertrendAtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 140.0)]
        [Display(Name = "Key Value", GroupName = "UT Bot", Order = 10)]
        public double UtKeyValue { get; set; }

        [NinjaScriptProperty]
        [Range(1, 140)]
        [Display(Name = "ATR Period", GroupName = "UT Bot", Order = 11)]
        public int UtAtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Heikin-Ashi Source", GroupName = "UT Bot", Order = 12)]
        public bool UseHeikinAshiSource { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Longs", GroupName = "Trade Rules", Order = 20)]
        public bool EnableLongs { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Shorts", GroupName = "Trade Rules", Order = 21)]
        public bool EnableShorts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Close On Disagreement", GroupName = "Trade Rules", Order = 22)]
        public bool CloseOnDisagreement { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use ATR Stop/Target", GroupName = "Risk Management", Order = 30)]
        public bool UseAtrRiskExits { get; set; }

        [NinjaScriptProperty]
        [Range(1, 140)]
        [Display(Name = "Risk ATR Period", GroupName = "Risk Management", Order = 31)]
        public int RiskAtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 100.0)]
        [Display(Name = "Stop ATR Multiple", GroupName = "Risk Management", Order = 32)]
        public double StopAtrMultiple { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 100.0)]
        [Display(Name = "Target ATR Multiple", GroupName = "Risk Management", Order = 33)]
        public double TargetAtrMultiple { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Signal Markers", GroupName = "Visuals", Order = 40)]
        public bool ShowSignalMarkers { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Trailing Lines", GroupName = "Visuals", Order = 41)]
        public bool ShowTrailingLines { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 100.0)]
        [Display(Name = "Hard Max Stop (points)", GroupName = "Loss Controls", Order = 50)]
        public double MaxStopPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "One-Bar Confirmation", GroupName = "Loss Controls", Order = 51)]
        public bool RequireOneBarConfirmation { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Chop Filter", GroupName = "Loss Controls", Order = 52)]
        public bool UseChopFilter { get; set; }

        [NinjaScriptProperty]
        [Range(2, 20)]
        [Display(Name = "Flip Lookback Bars", GroupName = "Loss Controls", Order = 53)]
        public int ChopLookbackBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10)]
        [Display(Name = "Max Direction Flips", GroupName = "Loss Controls", Order = 54)]
        public int MaxDirectionFlips { get; set; }

        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "Cooldown Bars After Loss", GroupName = "Loss Controls", Order = 55)]
        public int CooldownBarsAfterLoss { get; set; }

        [NinjaScriptProperty]
        [Range(25.0, 5000.0)]
        [Display(Name = "Max Daily Realized Loss ($)", GroupName = "Loss Controls", Order = 56)]
        public double MaxDailyRealizedLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Breakeven", GroupName = "Trade Protection", Order = 60)]
        public bool UseBreakeven { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 100.0)]
        [Display(Name = "Breakeven Trigger (points)", GroupName = "Trade Protection", Order = 61)]
        public double BreakevenTriggerPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 20.0)]
        [Display(Name = "Breakeven Plus (points)", GroupName = "Trade Protection", Order = 62)]
        public double BreakevenPlusPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Profit Trail", GroupName = "Trade Protection", Order = 63)]
        public bool UseProfitTrail { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 200.0)]
        [Display(Name = "Trail Trigger (points)", GroupName = "Trade Protection", Order = 64)]
        public double TrailTriggerPoints { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 200.0)]
        [Display(Name = "Trail Distance (points)", GroupName = "Trade Protection", Order = 65)]
        public double TrailDistancePoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use X-Trend Filter", GroupName = "X-Trend Filter", Order = 70)]
        public bool UseXTrendFilter { get; set; }

        [NinjaScriptProperty]
        [Range(2, 100)]
        [Display(Name = "WaveTrend Channel Length", GroupName = "X-Trend Filter", Order = 71)]
        public int WaveTrendChannelLength { get; set; }

        [NinjaScriptProperty]
        [Range(2, 100)]
        [Display(Name = "WaveTrend Average Length", GroupName = "X-Trend Filter", Order = 72)]
        public int WaveTrendAverageLength { get; set; }

        [NinjaScriptProperty]
        [Range(1, 4)]
        [Display(Name = "Minimum Aligned Timeframes", GroupName = "X-Trend Filter", Order = 73)]
        public int MinimumAlignedTimeframes { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Minimum Intraday Trend Score", GroupName = "X-Trend Filter", Order = 74)]
        public double MinimumIntradayTrendScore { get; set; }
        #endregion
    }
}
