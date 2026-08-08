# CurrencyRatioExchange

An ExileCore plugin for Path of Exile 1 that adds a ratio calculator beside the Currency Exchange window.

## Features

- Calculates the largest whole trade from the currency you own.
- Calculates how much currency to offer when an exact amount forms a whole trade at the chosen ratio.
- Reads competing exchange ratios and enables match/undercut quick-fill actions only for clean whole trades.
- Fills the exchange window automatically.
- Supports `Want:Have` ratios such as `1:3` and `2:5`.
- Optional counting from server-side stash inventories, with a visible-tab fallback.

## Installation

Place this repository in `Plugins/Source/CurrencyRatioExchange` under your PoE1 ExileCore installation, then enable the plugin in ExileCore.

## Build

```powershell
dotnet build
```

The project targets .NET 10 and resolves `ExileCore.dll` and `GameOffsets.dll` from the ExileCore installation containing the plugin.

## License

[MIT](LICENSE)
