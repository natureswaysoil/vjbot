# NinjaTrader -> GitHub Results Workflow

This workflow lets Strategy Analyzer exports from NinjaTrader 8 be copied into this repository and pushed to GitHub automatically from the trading PC.

## 1. Clone the repository on the NinjaTrader PC

Open PowerShell and run:

```powershell
cd $env:USERPROFILE\Documents
git clone https://github.com/natureswaysoil/vjbot.git
```

Default local path used by the sync script:

`C:\Users\<you>\Documents\vjbot`

## 2. Confirm GitHub authentication

From PowerShell:

```powershell
cd $env:USERPROFILE\Documents\vjbot
git pull
git push
```

If GitHub asks you to sign in, complete the browser sign-in once.

## 3. Export NinjaTrader Strategy Analyzer results

After each backtest, export the Strategy Analyzer grid/report into:

`C:\Users\<you>\Documents\NinjaTrader 8\export`

CSV or XLSX is preferred. TXT and XML are also accepted by the watcher.

## 4. Start automatic syncing

From PowerShell:

```powershell
cd $env:USERPROFILE\Documents\vjbot
powershell -ExecutionPolicy Bypass -File .\Tools\Sync-NinjaTraderResults.ps1
```

The script watches the NinjaTrader export folder. When a supported file is created or changed, it copies a timestamped version into `Results`, commits it, and pushes it to `main`.

## 5. If your folders are different

Example:

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\Sync-NinjaTraderResults.ps1 `
  -RepoPath "D:\Trading\vjbot" `
  -ExportPath "D:\NinjaExports"
```

## Recommended test discipline

Use the same MES contract/data assumptions, chart timeframe, date range, quantity, commission, and slippage assumptions when comparing strategy versions. This is necessary to determine whether X-Trend or another filter genuinely reduces the large losses rather than benefiting from a different test setup.

## Current comparison set

1. Original VJ2 + UT Bot
2. `VJ2UTBotLossFirstStrategy`
3. `VJ2UTBotLossFirstXTrendStrategy`

Loss containment is evaluated before maximum net profit.
