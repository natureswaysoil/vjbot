# NinjaTrader Results

Store Strategy Analyzer exports and trade logs here so each strategy revision can be compared against the same test period.

## Recommended naming

`YYYY-MM-DD_strategy_instrument_timeframe_period.ext`

Example:

`2026-08-29_VJ2UTBotLossFirstXTrend_MES_5m_2026YTD.xlsx`

## Minimum metrics to preserve

For every backtest, keep enough information to recover:

- Strategy name/version
- Instrument and contract
- Bar type/timeframe
- Test start and end dates
- Quantity
- Commission/slippage assumptions
- Net profit
- Gross profit
- Gross loss
- Profit factor
- Maximum drawdown
- Largest winning trade
- Largest losing trade
- Average trade
- Average winning trade
- Average losing trade
- Percent profitable
- Total number of trades
- Long vs short results

## Loss-first ranking

Compare versions in this order:

1. Largest losing trade
2. Maximum drawdown
3. Gross loss
4. Average losing trade
5. Profit factor
6. Net profit

A version should not be considered better merely because net profit is higher if it materially increases large-loss outliers or drawdown.
