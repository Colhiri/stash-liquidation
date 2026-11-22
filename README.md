# Stash Liquidation

[Русская версия](README.ru.md)

A quick-sell mod for single-player SPT. Sell loot from the stash or from containers inside it using the item's context menu. Ragman sends a single message with the proceeds after the standard flea market commission, when commission is enabled.

## Compatibility

- SPT **4.0.13** and Tarkov build **0.16.9.40087**
- [UI Fixes 5.3.9](https://github.com/tyfon7/UIFixes/releases/tag/v5.3.9), installed separately
- Starting with 1.1.7, the mod's menu, notifications and new receipts follow the game language. Russian uses Russian text. All other game languages use English.

## Installation

1. Close the game and SPT server.
2. Install the compatible version of UI Fixes.
3. Download the ZIP from the [latest release](https://github.com/Colhiri/stash-liquidation/releases/latest).
4. Extract the ZIP into the root of the SPT installation so its `BepInEx` and `SPT` folders merge with the existing folders.
5. Start the SPT server and game.

## Usage

Use UI Fixes to select items in the stash or in an open stash container. Hold **Shift** and left-click items to add them to the selection. Right-click one of the selected items and choose **Sell selected (N)**. For a single item or a container with loot, choose **Quick sell**. Collect the payment from Ragman's message.

- Eligible detachable parts are priced separately. A weapon's base price subtracts the value of its stock attachments. The sale price accounts for wear and remaining consumable resources.
- Built-in armor stays attached. Eligible removable plates and accessories can sell on their own.
- Items that cannot be listed on the flea market stay in your stash. Eligible parts can sell separately if the mod can detach them safely.
- Unpack ammo boxes manually before selling the loose rounds. Closed packs remain intact during bulk sales.
- You can sell unlocked contents of a locked container. Locked or pinned items are protected, along with the containers needed to hold them.
- Quick sales use no flea offer slots. Ordinary listings use the server's existing limit.

The game client sends the selected item IDs and the receipt language to its SPT server. The mod makes no external service requests and collects no telemetry.

## Build

Use the .NET 9 SDK and a local SPT 4.0.13 installation with UI Fixes 5.3.9 installed. The build reads the game and mod references from that installation.

```powershell
dotnet build Client/StashLiquidation.Client.csproj -c Release -p:SptRoot="D:/SPT"
dotnet build Server/StashLiquidation.Server.csproj -c Release -p:SptRoot="D:/SPT"
```

The client DLL goes in `BepInEx/plugins/StashLiquidation`. The server DLL goes in `SPT/user/mods/StashLiquidation`.

To uninstall, close the game and server and remove those two `StashLiquidation` folders.

Stash Liquidation uses the [MIT license](LICENSE).
