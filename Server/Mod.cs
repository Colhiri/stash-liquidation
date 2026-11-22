using SPTarkov.Server.Core.Models.Spt.Mod;

namespace StashLiquidation;

public sealed record Metadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.colhirux.stashliquidation";
    public string Name { get; init; } = "StashLiquidation";
    public string Author { get; init; } = "ColhiruX";
    public SemanticVersioning.Version Version { get; init; } = new("1.2.0");
    public SemanticVersioning.Range SptVersion { get; init; } = new("=4.1.6");
    public bool HasPrepatcher { get; init; }
    public string License { get; init; } = "MIT";
    public List<string>? Contributors { get; init; }
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; } = new()
    {
        ["com.tyfon.uifixes"] = new(">=6.0.4 <7.0.0")
    };
    public string? Url { get; init; } = "https://github.com/Colhiri/stash-liquidation";
}
