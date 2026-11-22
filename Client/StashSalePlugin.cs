using System;
using System.Linq;
using BepInEx;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Ragfair;
using HarmonyLib;
using UIFixes;
using UnityEngine;

namespace StashLiquidation;

[BepInPlugin("com.colhirux.stashliquidation", "ColhiruX-StashLiquidation", "1.2.0")]
[BepInProcess("EscapeFromTarkov.exe")]
[BepInDependency("com.SPT.core")]
[BepInDependency("com.tyfon.uifixes", "6.0.4")]
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

    private static void Postfix(ItemUiContext __instance, ItemContext itemContext, bool __state,
        InventoryController ____inventoryController, ContextInteractions<EItemInfoButton> __result)
    {
        if (!__state || saleInProgress || Plugin.InRaid() || __result == null
            || !__result.AllInteractions.Contains(EItemInfoButton.AddOffer)
            || !__result.IsActive(EItemInfoButton.AddOffer)
            || __instance.Session?.RagFair?.Available != true || RagFair.IsAddOffersTemporaryDisabled()
            || ____inventoryController is not BackEndInventoryController player
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
            Resources.Load<Sprite>("Characteristics/Icons/AddOffer"));
    }

    private static void Sell(BackEndInventoryController player, Stash stash, Item[] selection)
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
        var descendants = roots.SelectMany(item => item.GetAllItems()).Concat(roots)
            .Select(item => item.Id.ToString()).ToHashSet();
        bool russian = IsRussian();
        saleInProgress = true;
        try
        {
            player._backendSession.SendOperationRightNow(
                new
                {
                    Action = SaleProtocol.Action,
                    items = selected,
                    locale = russian ? LocalizationManager.RUSSIAN_LOCALIZATION : LocalizationManager.ENGLISH_LOCALIZATION
                },
                result =>
                {
                    saleInProgress = false;
                    if (!result.Succeed)
                    {
                        Debug.LogWarning($"Stash Liquidation sale failed: {result.Error}");
                        NotificationManager.DisplayWarningNotification(
                            russian ? "Продажа не прошла." : "The sale failed.");
                        return;
                    }

                    var remaining = stash.GetAllItems().Select(item => item.Id.ToString()).ToHashSet();
                    int sold = descendants.Count(id => !remaining.Contains(id));
                    if (sold == 0)
                    {
                        int newLoose = stash.Grid.Items.Count(item => descendants.Contains(item.Id.ToString())
                            && !selected.Contains(item.Id.ToString()));
                        if (newLoose > 0)
                        {
                            NotificationManager.DisplayMessageNotification(
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
                            NotificationManager.DisplayWarningNotification(error);
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
                    NotificationManager.DisplayMessageNotification(message);
                });
        }
        catch (Exception error)
        {
            saleInProgress = false;
            Debug.LogException(error);
            NotificationManager.DisplayWarningNotification(
                russian ? "Не удалось отправить запрос на продажу." : "Could not send the sale request.");
        }
    }

    private static bool IsRussian() =>
        LocalizationManager.Instance.Culture == LocalizationManager.RUSSIAN_LOCALIZATION;

    private static bool InStash(Item item, Stash stash)
    {
        if (item.PinLockState is EItemPinLockState.Pinned or EItemPinLockState.Locked)
        {
            return false;
        }

        while (item.CurrentAddress is { } address && address is not OwnerItself
            && address.Container is { ParentItem: { } parent } container)
        {
            if (parent == stash)
            {
                return container == stash.Grid;
            }

            if (parent is AmmoBox or SingleUseContainer
                || container is Slot slot && (slot.Locked || item.IsUnremovable))
            {
                return false;
            }

            item = parent;
        }

        return false;
    }
}
