using UnrealBuildTool;
public class GroomExportBridge : ModuleRules
{
    public GroomExportBridge(ReadOnlyTargetRules Target) : base(Target)
    {
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        PublicDependencyModuleNames.AddRange(new[] { "Core", "CoreUObject", "Engine" });
        PrivateDependencyModuleNames.AddRange(new[] { "HairStrandsCore", "MeshDescription", "Json", "DerivedDataCache" });
    }
}
