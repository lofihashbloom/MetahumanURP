#pragma once
#include "Kismet/BlueprintFunctionLibrary.h"
#include "GroomDataExportLibrary.generated.h"

class UGroomComponent;

// Read-only source data interchange. No renderer code or material graphs.
UCLASS()
class GROOMEXPORTBRIDGE_API UGroomDataExportLibrary : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable, Category="MetaHuman Interchange")
    static void ConfigureReferenceGroom(UGroomComponent* Component);

    UFUNCTION(BlueprintCallable, Category="MetaHuman Interchange")
    static bool ExportGroomData(const FString& AssetPath, const FString& FilePath);

    // Editor-only raw platform-buffer snapshot plus independently verified source mapping.
    // The original source asset and existing MHGRM001 interchange are never saved/rewritten.
    UFUNCTION(BlueprintCallable, Category="MetaHuman Interchange")
    static bool ExportBuiltGroomAttributes(const FString& AssetPath, const FString& OutputDirectory);
};
