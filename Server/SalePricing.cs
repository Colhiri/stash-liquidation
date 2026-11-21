using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;

namespace StashLiquidation;

#pragma warning disable CS0618 // SPT 4.0.13 registers configuration through ConfigServer.
[Injectable(InjectionType.Scoped)]
public class SalePricing(
    RagfairController flea,
    RagfairOfferService offers,
    RagfairServerHelper fleaRules,
    PaymentHelper payment,
    DatabaseService database,
    HandbookHelper handbook,
    ItemHelper itemHelper,
    PresetHelper presets,
    RagfairTaxService tax,
    ConfigServer configs)
{
    private readonly RagfairConfig fleaConfig = configs.GetConfig<RagfairConfig>();
#pragma warning restore CS0618

    internal bool TryQuote(IReadOnlyList<Item> lot, PmcData quoteProfile, Dictionary<MongoId, double> prices,
        out long gross, out long fee)
    {
        gross = 0;
        fee = 0;
        var part = lot[0];
        var template = itemHelper.GetItem(part.Template);
        if (part.Template.ToString() is SaleProtocol.Rubles or SaleProtocol.Dollars or SaleProtocol.Euros
            || template.Value?.Properties is not { CanSellOnRagfair: true } properties
            || properties.QuestItem == true || properties.IsUnsaleable == true
            || !fleaRules.IsItemValidRagfairItem(template)
            || lot.Skip(1).Any(item => itemHelper.GetItem(item.Template).Value?.Properties?.CanSellOnRagfair != true)
            || (database.GetGlobals().Configuration.RagFair.IsOnlyFoundInRaidAllowed
                && part.Upd?.SpawnedInSession != true))
        {
            return false;
        }

        double price = GetMarketPrice(part.Template, prices);
        if (price <= 0)
        {
            return false;
        }

        if (itemHelper.IsOfBaseclass(part.Template, BaseClasses.WEAPON))
        {
            var preset = presets.GetDefaultPreset(part.Template) ?? presets.GetPresets(part.Template)?.FirstOrDefault();
            if (preset?.Items is { Count: > 1 } stock)
            {
                double equipmentCost = stock.Where(item => item.Id != preset.Parent).Sum(item =>
                {
                    double value = GetMarketPrice(item.Template, prices);
                    return (value > 0 ? value : handbook.GetTemplatePrice(item.Template))
                        * (item.Upd?.StackObjectsCount ?? 1);
                });
                price = Math.Max(1, price - equipmentCost);
            }
        }

        price *= GetLotCondition(lot, properties);
        double count = part.Upd?.StackObjectsCount ?? 1;
        double valueForStack = Math.Round(price * count, MidpointRounding.AwayFromZero);
        if (!double.IsFinite(valueForStack) || valueForStack < 1 || valueForStack > int.MaxValue
            || count < 1 || count > int.MaxValue || count != Math.Truncate(count))
        {
            return false;
        }

        if (fleaConfig.Sell.Fees)
        {
            var detached = part with { Upd = part.Upd is { } upd ? upd with { } : new Upd() };
            quoteProfile.Inventory!.Items!.Add(detached);
            quoteProfile.Inventory.Items.AddRange(lot.Skip(1));
            double taxForStack = Math.Round(tax.CalculateTax(detached, quoteProfile, price, (int)count, false));
            if (!double.IsFinite(taxForStack) || taxForStack < 0 || taxForStack > int.MaxValue)
            {
                return false;
            }

            fee = (long)taxForStack;
        }

        gross = (long)valueForStack;
        return true;
    }

    private double GetLotCondition(IReadOnlyList<Item> lot, TemplateItemProperties properties)
    {
        double rootCondition = GetConditionMultiplier(lot[0].Upd, properties);
        if (lot.Count == 1)
        {
            return rootCondition;
        }

        var durableParts = lot.Select(item => (Item: item, Properties: itemHelper.GetItem(item.Template).Value?.Properties))
            .Where(part => part.Properties is not null && part.Item.Upd?.Repairable is { Durability: not null } state
                && (part.Properties.MaxDurability is > 0 || state.MaxDurability is > 0))
            .Select(part => GetConditionMultiplier(part.Item.Upd, part.Properties!));
        return durableParts.DefaultIfEmpty(rootCondition).Average();
    }

    private static double GetConditionMultiplier(Upd? state, TemplateItemProperties properties)
    {
        var maximumDurability = properties.MaxDurability is > 0
            ? properties.MaxDurability : state?.Repairable?.MaxDurability;
        if (maximumDurability is > 0 && state?.Repairable?.Durability is { } durability)
        {
            // SPT uses the square root of the remaining durability fraction for repairable gear.
            return Math.Sqrt(Math.Clamp(durability / maximumDurability.Value, 0, 1));
        }

        double remainingFraction = state switch
        {
            { MedKit.HpResource: { } hp } when properties.MaxHpResource is > 0 =>
                hp / properties.MaxHpResource.Value,
            { FoodDrink.HpPercent: { } amount } when properties.MaxResource is > 0 =>
                amount / properties.MaxResource.Value,
            { RepairKit.Resource: { } resource } when properties.MaxRepairResource is > 0 =>
                resource / properties.MaxRepairResource.Value,
            { Key.NumberOfUsages: { } used } when properties.MaximumNumberOfUsage is > 0 =>
                (properties.MaximumNumberOfUsage.Value - used) / (double)properties.MaximumNumberOfUsage.Value,
            { Resource.Value: { } resource } when properties.MaxResource is > 0 =>
                resource / properties.MaxResource.Value,
            { SideEffect.Value: { } resource } when properties.MaxResource is > 0 =>
                resource / properties.MaxResource.Value,
            _ => 1
        };

        return Math.Clamp(remainingFraction, 0, 1);
    }

    private double GetMarketPrice(MongoId template, Dictionary<MongoId, double> prices)
    {
        if (prices.TryGetValue(template, out var price))
        {
            return price;
        }

        bool hasCashOffers = offers.GetOffersOfType(template)?.Any(offer => !offer.IsTraderOffer()
            && offer.Requirements is { } requirements
            && requirements.Any() && requirements.All(requirement => payment.IsMoneyTpl(requirement.TemplateId))) == true;
        database.GetPrices().TryGetValue(template, out price);
        if (hasCashOffers)
        {
            double? average = flea.GetItemMinAvgMaxFleaPriceValues(new GetMarketPriceRequestData
            {
                TemplateId = template
            }).Avg;
            if (average is > 0 && double.IsFinite(average.Value))
            {
                price = average.Value;
            }
        }

        price = double.IsFinite(price) && price > 0 ? price : 0;
        prices.Add(template, price);
        return price;
    }
}
