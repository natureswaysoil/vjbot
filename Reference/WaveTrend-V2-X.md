# Wave Trend -V2-X reference

This file records the logic from the TradingView `Wave Trend -V2-X` indicator that was supplied for analysis. It is a reference summary rather than a verbatim copy of the Pine source.

## Core WaveTrend logic

The indicator uses:

- `ap = hlc3`
- channel length `n1 = 10`
- average length `n2 = 21`
- `esa = EMA(ap, n1)`
- `d = EMA(abs(ap - esa), n1)`
- `ci = (ap - esa) / (0.015 * d)`
- `wt1 = EMA(ci, n2)`
- `wt2 = SMA(wt1, 4)`

Direction is bullish when `wt1 > wt2` and bearish when `wt1 < wt2`.

## Original multi-timeframe concept

The supplied script evaluates WaveTrend direction on:

- 5 minute
- 15 minute
- 30 minute
- 1 hour
- 2 hour
- 4 hour
- 12 hour
- 1 day
- 1 week

It combines those directional readings into a weighted `SEEKER` score and labels the composite trend as BUY or SELL. Trend strength is categorized roughly as:

- WEAK: absolute score below 14
- MID: absolute score around 14 to 30
- STRONG: absolute score above 30

## MES adaptation used in this repository

For the NinjaTrader MES strategy, the filter was intentionally simplified to the intraday timeframes:

- 5m
- 15m
- 30m
- 1h

The daily and weekly components were excluded because their large weights can make an intraday strategy too slow to recognize valid reversals.

The default NinjaTrader filter requires at least 3 of the 4 intraday timeframes to align and a minimum composite score of 3.0.

## Risk-control decision

The original indicator also estimates price targets and a stop from the average movement between recent WaveTrend crosses. That stop logic is not used as the primary risk control in VJBot. The NinjaTrader strategies instead use a capped ATR stop, a hard maximum stop, breakeven logic, trailing protection, cooldowns after losses, and a daily loss lockout.

## Purpose

WaveTrend/X-Trend is used as a trade-permission filter. VJ2 Supertrend + UT Bot remain the entry timing mechanism. The objective is to reject low-quality/choppy entries without eliminating the larger trend trades.
