using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Utils.Json.Converters;

namespace StashLiquidation;

public record Metadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.colhirux.stashliquidation";
    public override string Name { get; init; } = "StashLiquidation";
    public override string Author { get; init; } = "ColhiruX";
    public override SemanticVersioning.Version Version { get; init; } = new("1.1.6");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("=4.0.13");
    public override string License { get; init; } = "MIT";
    public override List<string>? Contributors { get; init; }
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public override string? Url { get; init; } = "https://github.com/Colhiri/stash-liquidation";
    public override bool? IsBundleMod { get; init; }
}

[Injectable(TypePriority = OnLoadOrder.PostSptModLoader + 100)]
public class InitializeMod : IOnLoad
{
    public Task OnLoad()
    {
        BaseInteractionRequestDataConverter.RegisterModDataHandler(SaleProtocol.Action,
            json => JsonSerializer.Deserialize<SaleRequest>(json));

        return Task.CompletedTask;
    }
}
