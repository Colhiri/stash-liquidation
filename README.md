# Stash Liquidation

[Русская версия](README.ru.md)

A quick-sell mod for single-player SPT. Sell loot from the stash or from containers inside it using the item's context menu. Ragman sends a single message with the proceeds after the standard flea market commission, when commission is enabled.

## Compatibility

| SPT | Stash Liquidation | Required UI Fixes | Server folder |
| --- | --- | --- | --- |
| 4.1.6 (EFT 0.16.9.5.40743) | 1.2.0 | [6.0.4](https://github.com/tyfon7/UIFixes/releases/tag/v6.0.4) | `SPT_Runtime` |
| 4.0.13 (EFT 0.16.9.40087) | [1.1.7](https://github.com/Colhiri/stash-liquidation/releases/tag/v1.1.7) | [5.3.9](https://github.com/tyfon7/UIFixes/releases/tag/v5.3.9) | `SPT` |

Use the archive for your SPT version. The mod's menu, notifications and new receipts use Russian when the game is set to Russian and English for other languages.

## Installation

1. Close the game and SPT server.
2. Install the compatible version of UI Fixes.
3. Download the ZIP for your SPT version from [Releases](https://github.com/Colhiri/stash-liquidation/releases).
4. Extract the ZIP into the root of your SPT installation, alongside `EscapeFromTarkov.exe`. Merge `BepInEx` and the matching server folder with the existing folders.
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

Use the .NET 10 SDK and a local SPT 4.1.6 installation with UI Fixes 6.0.4 installed. The build reads the client assemblies patched by the SPT launcher from that installation. To build for SPT 4.0.13, use the [v1.1.7 source](https://github.com/Colhiri/stash-liquidation/tree/v1.1.7).

```powershell
dotnet build Client/StashLiquidation.Client.csproj -c Release -p:SptRoot="D:/SPT-4.1.6"
dotnet build Server/StashLiquidation.Server.csproj -c Release -p:SptRoot="D:/SPT-4.1.6"
```

The client DLL goes in `BepInEx/plugins/StashLiquidation`. The 4.1.6 server DLL goes in `SPT_Runtime/user/mods/StashLiquidation`, and the 4.0.13 server DLL goes in `SPT/user/mods/StashLiquidation`.

To uninstall, close the game and server and remove those two `StashLiquidation` folders.

Stash Liquidation uses the [MIT license](LICENSE).
