# VJBot

NinjaTrader 8 strategy development for MES using VJ2 Supertrend + UT Bot confirmation, with a loss-first risk-management focus.

## Current strategy versions

- `Strategies/VJ2UTBotLossFirstStrategy.cs` — VJ2 + UT Bot with capped ATR stop, hard max stop, breakeven, trailing protection, chop filter, cooldown after losses, and daily loss lockout.
- `Strategies/VJ2UTBotLossFirstXTrendStrategy.cs` — adds a simplified intraday WaveTrend/X-Trend filter using 5m, 15m, 30m and 1h confirmation.
- `Reference/WaveTrend-V2-X.md` — TradingView WaveTrend/X-Trend reference source supplied for analysis.

## Initial MES test settings

Primary chart: 5-minute MES, 1 contract.

Loss controls:
- Hard max stop: 15 points (about $75 per MES contract before slippage/commission)
- ATR stop: 1.0 ATR, capped at 15 points
- Breakeven trigger: +10 points
- Breakeven lock: +1 point
- Profit trail trigger: +20 points
- Trail distance: 10 points
- One-bar entry confirmation: enabled
- Cooldown after loss: 3 bars
- Daily realized-loss lockout: $175

X-Trend version defaults:
- WaveTrend channel length: 10
- WaveTrend average length: 21
- Timeframes: 5m, 15m, 30m, 1h
- Minimum aligned timeframes: 3 of 4
- Minimum intraday trend score: 3.0

## Evaluation priority

Rank changes primarily by:
1. Largest losing trade
2. Maximum drawdown
3. Gross loss
4. Average losing trade
5. Profit factor
6. Net profit

The goal is to reduce large loss outliers and drawdown without destroying the profitable trend trades.
