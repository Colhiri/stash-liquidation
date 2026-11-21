using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils.Json.Converters;

namespace StashLiquidation;

public record Metadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "local.tarkov.stashliquidation";
    public override string Name { get; init; } = "Stash Liquidation";
    public override string Author { get; init; } = "Stash Liquidation";
    public override SemanticVersioning.Version Version { get; init; } = new("1.1.4");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("=4.0.13");
    public override string License { get; init; } = "MIT";
    public override List<string>? Contributors { get; init; }
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public override string? Url { get; init; }
    public override bool? IsBundleMod { get; init; }
}

[Injectable(TypePriority = OnLoadOrder.PostSptModLoader + 100)]
public class InitializeMod(DatabaseService database) : IOnLoad
{
    public Task OnLoad()
    {
        BaseInteractionRequestDataConverter.RegisterModDataHandler(SaleProtocol.Action,
            json => JsonSerializer.Deserialize<SaleRequest>(json));

        var fleaSettings = database.GetGlobals().Configuration.RagFair;
        fleaSettings.MaxActiveOfferCount =
        [
            new() { From = float.MinValue, To = float.MaxValue,
                Count = int.MaxValue, CountForSpecialEditions = int.MaxValue }
        ];
        return Task.CompletedTask;
    }
}
