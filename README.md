# Stash Liquidation

A quick-sell mod for single-player SPT. Sell loot from the stash or from containers inside it using the item's context menu. Ragman sends a single message with the proceeds after the standard flea market commission, when commission is enabled.

## Compatibility

- SPT **4.0.13** and Tarkov build **0.16.9.40087**
- [UI Fixes 5.3.9](https://github.com/tyfon7/UIFixes/releases/tag/v5.3.9), installed separately
- The custom context menu labels and sale receipts are in Russian.

## Installation

1. Close the game and SPT server.
2. Install the compatible version of UI Fixes.
3. Download `StashLiquidation-1.1.4-SPT4.0.13.zip` from the [release page](https://github.com/Colhiri/stash-liquidation/releases/tag/v1.1.4).
4. Extract the ZIP into the root of the SPT installation so its `BepInEx` and `SPT` folders merge with the existing folders.
5. Start the SPT server and game.

## Usage

Select items in the stash or in an open stash container, then right-click one of the selected items and choose **Продать выделенное** (Sell selected). For a single item, choose **Быстро продать** (Quick sell). Collect the payment from Ragman's message.

- Eligible detachable parts are priced separately. A weapon's base price subtracts the value of its stock attachments. The sale price accounts for wear and remaining consumable resources.
- Built-in armor stays attached. Eligible removable plates and accessories can sell on their own.
- Unpack ammo boxes manually before selling the loose rounds. Closed packs remain intact during bulk sales.
- You can sell unlocked contents of a locked container. Locked or pinned items are protected, along with the containers needed to hold them.
- The active flea listing limit is set to the maximum supported count.

## Build

Use the .NET 9 SDK and a local SPT 4.0.13 installation with UI Fixes 5.3.9 installed. The build reads the game and mod references from that installation.

```powershell
dotnet build Client/StashLiquidation.Client.csproj -c Release -p:SptRoot="D:/SPT"
dotnet build Server/StashLiquidation.Server.csproj -c Release -p:SptRoot="D:/SPT"
```

The client DLL goes in `BepInEx/plugins/StashLiquidation`. The server DLL goes in `SPT/user/mods/StashLiquidation`.

To uninstall, close the game and server and remove those two `StashLiquidation` folders.

Stash Liquidation uses the [MIT license](LICENSE).
