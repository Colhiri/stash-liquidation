using System;
using System.Linq;
using BepInEx;
using Comfort.Common;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using UIFixes;
using UnityEngine;

namespace StashLiquidation;

[BepInPlugin("com.colhirux.stashliquidation", "ColhiruX-StashLiquidation", "1.1.7")]
[BepInProcess("EscapeFromTarkov.exe")]
[BepInDependency("com.SPT.core")]
[BepInDependency("Tyfon.UIFixes", "5.3.9")]
public sealed class StashSalePlugin : BaseUnityPlugin
{
    private void Awake()
    {
        new Harmony(Info.Metadata.GUID).CreateClassProcessor(typeof(SaleContextMenu)).Patch();
    }
}

[HarmonyPatch(typeof(ItemUiContext), nameof(ItemUiContext.GetItemContextInteractions))]
internal static class SaleContextMenu
{
    private static bool saleInProgress;

    // The game replaces a null close callback before the postfix runs.
    [HarmonyPriority(Priority.First)]
    private static void Prefix(Action? closeAction, out bool __state)
    {
        __state = closeAction == null;
    }

    private static void Postfix(ItemUiContext __instance, ItemContextAbstractClass itemContext, bool __state,
        InventoryController ___inventoryController_0, ItemInfoInteractionsAbstractClass<EItemInfoButton> __result)
    {
        if (!__state || saleInProgress || Plugin.InRaid() || __result == null
            || !__result.AllInteractions.Contains(EItemInfoButton.AddOffer)
            || !__result.IsActive(EItemInfoButton.AddOffer)
            || __instance.Session?.RagFair?.Available != true || RagFairClass.IsAddOffersTemporaryDisabled()
            || ___inventoryController_0 is not GClass3387 player
            || player.Inventory?.Stash is not { } stash
            || !InStash(itemContext.Item, stash))
        {
            return;
        }

        var selected = MultiSelect.ItemContexts.Select(context => context.Item)
            .Where(item => InStash(item, stash))
            .GroupBy(item => item.Id)
            .Select(group => group.First())
            .ToArray();
        if (!MultiSelect.Enabled || !selected.Any(item => item.Id == itemContext.Item.Id))
        {
            selected = new[] { itemContext.Item };
        }

        bool russian = IsRussian();
        string title = selected.Length == 1
            ? (russian ? "Быстро продать" : "Quick sell")
            : (russian ? $"Продать выделенное ({selected.Length})" : $"Sell selected ({selected.Length})");
        __result.method_2(SaleProtocol.Action, title, () => Sell(player, stash, selected),
            CacheResourcesPopAbstractClass.Pop<Sprite>("Characteristics/Icons/AddOffer"));
    }

    private static void Sell(GClass3387 player, StashItemClass stash, Item[] selection)
    {
        if (saleInProgress || player.Inventory?.Stash != stash || Plugin.InRaid())
        {
            return;
        }

        var roots = selection.Where(item => InStash(item, stash)).ToArray();
        if (roots.Length == 0)
        {
            return;
        }

        string[] selected = roots.Select(item => item.Id.ToString()).ToArray();
        var descendants = roots.SelectMany(item => GClass3380.GetAllItems(item)).Concat(roots)
            .Select(item => item.Id.ToString()).ToHashSet();
        bool russian = IsRussian();
        saleInProgress = true;
        try
        {
            player.IBackEndSession.SendOperationRightNow(
                new
                {
                    Action = SaleProtocol.Action,
                    items = selected,
                    locale = russian ? LocaleManagerClass.RUSSIAN_LOCALIZATION : LocaleManagerClass.ENGLISH_LOCALIZATION
                },
                result =>
                {
                    saleInProgress = false;
                    if (!result.Succeed)
                    {
                        Debug.LogWarning($"Stash Liquidation sale failed: {result.Error}");
                        NotificationManagerClass.DisplayWarningNotification(
                            russian ? "Продажа не прошла." : "The sale failed.");
                        return;
                    }

                    var remaining = GClass3380.GetAllItems(stash).Select(item => item.Id.ToString()).ToHashSet();
                    int sold = descendants.Count(id => !remaining.Contains(id));
                    if (sold == 0)
                    {
                        int newLoose = stash.Grid.Items.Count(item => descendants.Contains(item.Id.ToString())
                            && !selected.Contains(item.Id.ToString()));
                        if (newLoose > 0)
                        {
                            NotificationManagerClass.DisplayMessageNotification(
                                russian ? $"Разобрано. Деталей оставлено в схроне: {newLoose}."
                                    : $"Gear disassembled. Parts left in stash: {newLoose}.");
                        }
                        else
                        {
                            bool onlyAmmoBoxes = roots.All(item => item is AmmoBox);
                            string error = onlyAmmoBoxes
                                ? (russian ? "Закрытые пачки патронов нужно вскрывать вручную."
                                    : "Unpack sealed ammo boxes before selling.")
                                : (russian ? "Продажа не состоялась. Проверь доступность предметов на барахолке, блокировку и место в схроне."
                                    : "Nothing sold. Check flea eligibility, locked items and available stash space.");
                            NotificationManagerClass.DisplayWarningNotification(error);
                        }
                        return;
                    }

                    int kept = descendants.Count - sold;
                    string message = russian
                        ? $"Продано предметов и деталей: {sold}. Рубли в письме Барахольщика."
                        : $"Items and parts sold: {sold}. Collect your roubles from Ragman.";
                    if (kept > 0)
                    {
                        message += russian ? $" Осталось в схроне: {kept}." : $" Left in stash: {kept}.";
                    }
                    NotificationManagerClass.DisplayMessageNotification(message);
                });
        }
        catch (Exception error)
        {
            saleInProgress = false;
            Debug.LogException(error);
            NotificationManagerClass.DisplayWarningNotification(
                russian ? "Не удалось отправить запрос на продажу." : "Could not send the sale request.");
        }
    }

    private static bool IsRussian() =>
        LocaleManagerClass.LocaleManagerClass.String_0 == LocaleManagerClass.RUSSIAN_LOCALIZATION;

    private static bool InStash(Item item, StashItemClass stash)
    {
        if (item.PinLockState is EItemPinLockState.Pinned or EItemPinLockState.Locked)
        {
            return false;
        }

        while (item.CurrentAddress is { } address && address is not GClass3390
            && address.Container is { ParentItem: { } parent } container)
        {
            if (parent == stash)
            {
                return container == stash.Grid;
            }

            if (parent is AmmoBox or RandomLootContainerItemClass
                || container is Slot slot && (slot.Locked || item.IsUnremovable))
            {
                return false;
            }

            item = parent;
        }

        return false;
    }
}
