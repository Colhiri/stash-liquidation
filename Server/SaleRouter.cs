using System.Globalization;
using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;

namespace StashLiquidation;

public record SaleRequest : BaseInteractionRequestData
{
    [JsonPropertyName("items")]
    public string[]? Items { get; init; }
}

[Injectable(InjectionType.Scoped)]
public class SaleRouter(
    SalePricing pricing,
    DatabaseService database,
    InventoryHelper inventoryHelper,
    ItemHelper itemHelper,
    MailSendService mail,
    QuestHelper questHelper,
    TimeUtil time) : ItemEventRouterDefinition
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    protected override List<HandledRoute> GetHandledRoutes() => [new(SaleProtocol.Action, false)];

    protected override ValueTask<ItemEventRouterResponse> HandleItemEventInternal(
        string url, PmcData pmc, BaseInteractionRequestData body, MongoId sessionId, ItemEventRouterResponse output)
    {
        if (body is not SaleRequest { Items.Length: > 0 } request || pmc.Inventory is not { } inventory)
        {
            return ValueTask.FromResult(output);
        }

        lock (inventory)
        {
            if (inventory.Items is not { } items || inventory.Stash is not { } stashId)
            {
                return ValueTask.FromResult(output);
            }

            var templates = database.GetItems();
            var itemsById = items.ToDictionary(item => item.Id.ToString());
            var children = items.ToLookup(item => item.ParentId);
            var protectedBranches = GetProtectedBranches(itemsById, templates);
            string stash = stashId.ToString();
            var saleRoots = request.Items.OfType<string>().Distinct()
                .Where(itemsById.ContainsKey).Select(id => itemsById[id])
                .Where(root => IsIndependentStashSelection(root, stash, itemsById))
                .SelectMany(root => GetSaleRoots(root, children, protectedBranches, templates))
                .DistinctBy(root => root.Id).ToArray();
            var saleRootIds = saleRoots.Select(root => root.Id.ToString()).ToHashSet();
            var plans = new List<Breakdown>();
            var prices = new Dictionary<MongoId, double>();
            var quoteItems = new List<Item>(1);
            var quoteProfile = pmc with { Inventory = inventory with { Items = quoteItems } };

            foreach (var root in saleRoots)
            {
                if (!IsIndependentStashSelection(root, stash, itemsById, saleRootIds))
                {
                    continue;
                }

                var lots = GetSaleLots(root, children).ToList();
                var parts = lots.SelectMany(lot => lot.Items).ToArray();

                var kept = new List<SaleLot>();
                long gross = 0;
                long fees = 0;
                foreach (var lot in lots)
                {
                    quoteItems.Clear();
                    if (!lot.IsSealed && pricing.TryQuote(lot.Items, quoteProfile, prices, out long value, out long fee))
                    {
                        gross += value;
                        fees += fee;
                    }
                    else
                    {
                        kept.Add(lot);
                    }
                }

                if (gross <= fees)
                {
                    kept = lots;
                    gross = 0;
                    fees = 0;
                }

                if (lots.Count > 1 || kept.Count == 0)
                {
                    plans.Add(new Breakdown(root, parts, kept, gross, fees));
                }
            }

            var remaining = items;
            var deletions = new List<DeletedItem>();
            var replacements = new List<Item>();
            var soldIds = new HashSet<MongoId>();
            long saleTotal = 0;
            long commission = 0;

            // Clear fully marketable lots first so retained parts can use the space they free.
            foreach (var plan in plans.OrderBy(plan => plan.Kept.Any(lot => lot.Root.Id != plan.Root.Id)))
            {
                var retained = plan.Kept.FirstOrDefault(lot => lot.Root.Id == plan.Root.Id);
                var idsStayingInPlace = retained?.Items.Select(item => item.Id).ToHashSet() ?? [];
                var removed = plan.Parts.Where(part => !idsStayingInPlace.Contains(part.Id)).ToArray();
                var removedIds = removed.Select(part => part.Id.ToString()).ToHashSet();
                var staged = remaining.Where(item => !removedIds.Contains(item.Id.ToString())).ToList();
                if (!TryPlaceRetainedParts(plan, pmc, stashId, staged, out var placed))
                {
                    continue;
                }

                staged.AddRange(placed);
                remaining = staged;

                // Delete each affected subtree once. Retained roots stay at their current address,
                // since the client only applies new items directly to its inventory roots.
                deletions.AddRange(removed.Where(part => !removedIds.Contains(part.ParentId ?? string.Empty))
                    .Select(part => new DeletedItem { Id = part.Id }));
                replacements.AddRange(placed);
                soldIds.UnionWith(removed.Select(part => part.Id).Except(placed.Select(part => part.Id)));
                saleTotal += plan.Gross;
                commission += plan.Fees;
            }

            if (deletions.Count == 0)
            {
                return ValueTask.FromResult(output);
            }

            var changes = output.ProfileChanges[sessionId].Items!;
            if (saleTotal > 0 && !SendPayout(sessionId, pmc, saleTotal, commission))
            {
                return ValueTask.FromResult(output);
            }

            changes.DeletedItems!.AddRange(deletions);
            changes.NewItems!.AddRange(replacements);
            inventory.Items = remaining;
            pmc.InsuredItems?.RemoveAll(item => item.ItemId is { } id && soldIds.Contains(id));
        }

        return ValueTask.FromResult(output);
    }

    private bool IsSealedPackage(Item item) =>
        itemHelper.IsOfBaseclass(item.Template, BaseClasses.AMMO_BOX)
        || itemHelper.IsOfBaseclass(item.Template, BaseClasses.RANDOM_LOOT_CONTAINER);

    private static bool IsProtected(Item item, Dictionary<MongoId, TemplateItem> templates) =>
        item.Upd?.PinLockState is PinLockState.Pinned or PinLockState.Locked
        || !templates.ContainsKey(item.Template);

    private static HashSet<string> GetProtectedBranches(Dictionary<string, Item> items,
        Dictionary<MongoId, TemplateItem> templates)
    {
        var branches = new HashSet<string>();
        foreach (var protectedItem in items.Values.Where(item => IsProtected(item, templates)))
        {
            var current = protectedItem;
            while (branches.Add(current.Id.ToString()) && current.ParentId is { } parentId
                && items.TryGetValue(parentId, out var parent))
            {
                current = parent;
            }
        }

        return branches;
    }

    private IEnumerable<Item> GetSaleRoots(Item root, ILookup<string?, Item> children,
        HashSet<string> protectedBranches, Dictionary<MongoId, TemplateItem> templates)
    {
        if (IsProtected(root, templates) || IsSealedPackage(root))
        {
            yield break;
        }

        if (!protectedBranches.Contains(root.Id.ToString()))
        {
            yield return root;
            yield break;
        }

        // Leave the containers holding protected items in place and process their detachable siblings.
        foreach (var child in children[root.Id.ToString()].Where(item => !IsFixedToParent(root, item)))
        {
            foreach (var eligible in GetSaleRoots(child, children, protectedBranches, templates))
            {
                yield return eligible;
            }
        }
    }

    private bool IsIndependentStashSelection(Item item, string stashId, Dictionary<string, Item> items,
        HashSet<string>? selectedIds = null)
    {
        var visited = new HashSet<string> { item.Id.ToString() };
        while (item.ParentId is { } parentId && visited.Add(parentId))
        {
            if (parentId == stashId)
            {
                return item.SlotId == "hideout";
            }

            if (selectedIds?.Contains(parentId) == true || !items.TryGetValue(parentId, out var parent)
                || IsSealedPackage(parent) || IsFixedToParent(parent, item))
            {
                return false;
            }

            item = parent;
        }

        return false;
    }

    private bool IsFixedToParent(Item parent, Item child)
    {
        var slot = itemHelper.GetItem(parent.Template).Value?.Properties?.Slots?.FirstOrDefault(definition =>
            string.Equals(definition.Name, child.SlotId, StringComparison.OrdinalIgnoreCase));
        return slot is not null && (slot.Properties?.Filters?.FirstOrDefault()?.Locked == true
            || itemHelper.GetItem(child.Template).Value?.Properties?.IsUnRemovable == true);
    }

    private IEnumerable<SaleLot> GetSaleLots(Item root, ILookup<string?, Item> children)
    {
        if (IsSealedPackage(root))
        {
            yield return new SaleLot(GetSubtree(root, children).ToList(), IsSealed: true);
            yield break;
        }

        var attached = new List<Item> { root };
        foreach (var child in children[root.Id.ToString()])
        {
            if (IsFixedToParent(root, child))
            {
                attached.AddRange(GetSubtree(child, children));
                continue;
            }

            foreach (var lot in GetSaleLots(child, children))
            {
                yield return lot;
            }
        }

        yield return new SaleLot(attached, IsSealed: attached.Skip(1).Any(IsSealedPackage));
    }

    private static IEnumerable<Item> GetSubtree(Item root, ILookup<string?, Item> children)
    {
        yield return root;
        foreach (var child in children[root.Id.ToString()])
        {
            foreach (var part in GetSubtree(child, children))
            {
                yield return part;
            }
        }
    }

    private bool TryPlaceRetainedParts(Breakdown plan, PmcData pmc, MongoId stashId, List<Item> staged,
        out List<Item> placed)
    {
        placed = [];
        var looseParts = plan.Kept.Where(lot => lot.Root.Id != plan.Root.Id).ToArray();

        if (looseParts.Length == 0)
        {
            return true;
        }

        var stash = staged.FirstOrDefault(item => item.Id == stashId);
        var grid = stash is null ? null : itemHelper.GetItem(stash.Template).Value?.Properties?.Grids?.FirstOrDefault()?.Properties;
        if (grid is null)
        {
            return false;
        }

        int width = grid.CellsH is > 0 ? grid.CellsH.Value : 10;
        int height = grid.CellsV is > 0 ? grid.CellsV.Value : 66;
        height += (int)(pmc.Bonuses?.FirstOrDefault(bonus => bonus.Type == BonusType.StashRows)?.Value ?? 0);
        int[,] slots = inventoryHelper.GetContainerMap(width, height, staged, stashId);

        foreach (var lot in looseParts)
        {
            var loose = new List<Item>(lot.Items) { [0] = lot.Root with { } };
            if (inventoryHelper.PlaceItemInContainer(slots, loose, stashId.ToString()).Success != true)
            {
                return false;
            }

            placed.AddRange(loose);
        }

        return true;
    }

    private bool SendPayout(MongoId sessionId, PmcData pmc, long gross, long commission)
    {
        MongoId rubles = SaleProtocol.Rubles;
        if (itemHelper.GetItem(rubles).Value?.Properties?.StackMaxSize is not > 0)
        {
            return false;
        }

        long payout = gross - commission;
        var attachments = itemHelper.SplitStack(new Item
        {
            Id = new MongoId(),
            Template = rubles,
            Upd = new Upd { StackObjectsCount = payout }
        });
        string message = $"Продажа из схрона. Сумма продажи: {gross.ToString("N0", Russian)} ₽. "
            + $"Комиссия: {commission.ToString("N0", Russian)} ₽. К получению: {payout.ToString("N0", Russian)} ₽.";
        int storageSeconds = time.GetHoursAsSeconds((int)questHelper.GetMailItemRedeemTimeHoursForProfile(pmc));
        mail.SendDirectNpcMessageToPlayer(sessionId, Traders.RAGMAN, MessageType.MessageWithItems,
            message, attachments, storageSeconds);
        return true;
    }

    private sealed record SaleLot(List<Item> Items, bool IsSealed)
    {
        public Item Root => Items[0];
    }

    private sealed record Breakdown(Item Root, Item[] Parts, List<SaleLot> Kept, long Gross, long Fees);
}
