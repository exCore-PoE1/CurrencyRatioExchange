# CurrencyRatioExchange

An ExileCore plugin for Path of Exile 1 that adds a ratio calculator beside the Currency Exchange window.

## Features

- Calculates the largest whole trade from the currency you own.
- Calculates how much currency to offer when buying an exact amount.
- Reads competing exchange ratios and provides match/undercut quick-fill actions.
- Fills the exchange window automatically.
- Supports `Want:Have` ratios such as `1:3` and `2:5`.
- Optional counting from the visible stash tab.

## Installation

Place this repository in `Plugins/Source/CurrencyRatioExchange` under your PoE1 ExileCore installation, then enable the plugin in ExileCore.

## Build

```powershell
dotnet build
```

The project targets .NET 10 and resolves `ExileCore.dll` and `GameOffsets.dll` from the ExileCore installation containing the plugin.

## License

[MIT](LICENSE)

