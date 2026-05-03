using System.Collections.Generic;
using SemanticVersioning;
using SPTarkov.Server.Core.Models.Spt.Mod;

namespace PathToTarkov;

/// <summary>
/// Provides mod identity to SPT's ModValidator.
/// AbstractModMetadata is a C# record, so this must also be a record.
/// </summary>
public record PttModMetadata : AbstractModMetadata
{
    public override string   ModGuid    { get; init; } = "com.guillaumearm.PathToTarkov";
    public override string   Name       { get; init; } = "Trap-PathToTarkov";
    public override string   Author     { get; init; } = "guillaumearm";
    public override Version  Version    { get; init; } = new Version(7, 0, 0);
    public override Range    SptVersion { get; init; } = new Range("~4.0.0");
    public override string   License    { get; init; } = "MIT";
    public override string?  Url        { get; init; } = "https://github.com/guillaumearm/PathToTarkov";

    public override List<string>              Contributors      { get; init; } = new();
    public override List<string>              Incompatibilities { get; init; } = new();
    public override Dictionary<string, Range> ModDependencies   { get; init; } = new();
    public override bool?                     IsBundleMod       { get; init; } = false;
}
