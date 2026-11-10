using UnrealBuildTool;

public class TextureExportBridge : ModuleRules
{
    public TextureExportBridge(ReadOnlyTargetRules Target) : base(Target)
    {
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        PublicDependencyModuleNames.AddRange(new[] { "Core", "CoreUObject", "Engine" });
        PrivateDependencyModuleNames.AddRange(new[] { "Json", "RHI" });
        if (Target.Platform == UnrealTargetPlatform.Win64) PublicSystemLibraries.Add("bcrypt.lib");
    }
}
