#pragma once

#include "Kismet/BlueprintFunctionLibrary.h"
#include "NativeTextureExportLibrary.generated.h"

/** Read-only source texture interchange through public Engine APIs. */
UCLASS()
class TEXTUREEXPORTBRIDGE_API UNativeTextureExportLibrary : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    /** Export the current Editor platform's complete available BC1/3/5/7 mip chain.
     * Use only in a dedicated export process: the public initial-mip API may
     * consume its in-memory bulk buffers. No source asset is saved or modified.
     */
    UFUNCTION(BlueprintCallable, Category="MetaHuman Interchange")
    static FString ExportNativeTexture(const FString& AssetPath, const FString& FilePath);
};
