#include "NativeTextureExportLibrary.h"

#include "Dom/JsonObject.h"
#include "Engine/Texture2D.h"
#include "HAL/FileManager.h"
#include "HAL/PlatformMisc.h"
#include "HAL/PlatformProperties.h"
#include "Misc/EngineVersion.h"
#include "Misc/FileHelper.h"
#include "Misc/PackageName.h"
#include "Misc/Paths.h"
#include "Modules/ModuleManager.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"
#include "TextureCompiler.h"
#include "TextureResource.h"
#include "String/BytesToHex.h"

#if PLATFORM_MAC
#include <CommonCrypto/CommonDigest.h>
#elif PLATFORM_WINDOWS
#include "Windows/AllowWindowsPlatformTypes.h"
#include <bcrypt.h>
#include "Windows/HideWindowsPlatformTypes.h"
#endif

IMPLEMENT_MODULE(FDefaultModuleImpl, TextureExportBridge)

namespace
{
    FString Sha256(const void* Data, int64 Size)
    {
        if (Size < 0 || Size > MAX_uint32) return FString();
        uint8 Digest[32];
#if PLATFORM_MAC
        if (!CC_SHA256(Data, static_cast<CC_LONG>(Size), Digest)) return FString();
#elif PLATFORM_WINDOWS
        BCRYPT_ALG_HANDLE Algorithm = nullptr;
        if (BCryptOpenAlgorithmProvider(&Algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) < 0) return FString();
        const auto Status = BCryptHash(Algorithm, nullptr, 0,
            const_cast<uint8*>(static_cast<const uint8*>(Data)), static_cast<ULONG>(Size), Digest, 32);
        BCryptCloseAlgorithmProvider(Algorithm, 0);
        if (Status < 0) return FString();
#else
        return FString();
#endif
        return BytesToHex(Digest, 32).ToLower();
    }

    struct FMipBuffers
    {
        TArray<void*> Pointers;
        TArray<int64> Sizes;
        explicit FMipBuffers(int32 Count)
        {
            Pointers.SetNumZeroed(Count);
            Sizes.SetNumZeroed(Count);
        }
        ~FMipBuffers()
        {
            for (void* Pointer : Pointers) FMemory::Free(Pointer);
        }
    };

    void AppendUint32LE(TArray<uint8>& Data, uint32 Value)
    {
        for (uint32 Byte = 0; Byte < 4; ++Byte) Data.Add(static_cast<uint8>(Value >> (8 * Byte)));
    }
}

FString UNativeTextureExportLibrary::ExportNativeTexture(const FString& AssetPath, const FString& FilePath)
{
    auto Fail = [](const FString& Reason) { return Reason; };
    if (!IsInGameThread()) return Fail(TEXT("Export must run on the Editor game thread."));
    UTexture2D* Texture = LoadObject<UTexture2D>(nullptr, *AssetPath);
    if (!Texture) return Fail(TEXT("Texture2D asset could not be loaded: ") + AssetPath);
    UTexture* Pending[] = { Texture };
    FTextureCompilingManager::Get().FinishCompilation(MakeArrayView(Pending));
    FTexturePlatformData* Platform = Texture->GetPlatformData();
    if (!Platform) return Fail(TEXT("No platform texture data is available."));
    if (Platform->VTData || Texture->VirtualTextureStreaming)
        return Fail(TEXT("Virtual textures are not supported by this block interchange format."));
    const TCHAR* Format = nullptr;
    int32 BlockBytes = 16;
    switch (Platform->PixelFormat)
    {
    case PF_DXT1: Format = TEXT("BC1_UNorm"); BlockBytes = 8; break;
    case PF_DXT5: Format = TEXT("BC3_UNorm"); break;
    case PF_BC5: Format = TEXT("BC5_UNorm"); break;
    case PF_BC7: Format = TEXT("BC7_UNorm"); break;
    default:
        return Fail(FString::Printf(TEXT("Unsupported native pixel format enum %d; expected BC1/3/5/7. No conversion was attempted."), static_cast<int32>(Platform->PixelFormat)));
    }
    if (Platform->PixelFormat == PF_BC5 && Texture->SRGB)
        return Fail(TEXT("BC5 interchange requires linear data (source SRGB must be false)."));
    if (Texture->CompressionSettings == TC_Normalmap && Platform->PixelFormat != PF_BC5)
        return Fail(TEXT("Native normal-map interchange supports BC5 only."));
    if (Platform->SizeX < 1 || Platform->SizeY < 1 || Platform->SizeX > 16384 || Platform->SizeY > 16384)
        return Fail(TEXT("Texture dimensions are outside the supported desktop interchange limits."));
    const int32 MipCount = Platform->Mips.Num();
    if (MipCount < 1 || MipCount > 15) return Fail(TEXT("Invalid available mip count."));
    // The virtual public method reports exact allocations, including mips loaded
    // from Editor derived data. We neither decode nor re-encode the BC blocks.
    FMipBuffers Mips(MipCount);
    if (!Texture->GetInitialMipData(0, MakeArrayView(Mips.Pointers), MakeArrayView(Mips.Sizes)))
        return Fail(TEXT("Public initial-mip API could not provide the complete available mip chain."));

    TArray<uint8> Payload;
    TArray<TSharedPtr<FJsonValue>> MipMetadata;
    for (int32 Level = 0; Level < MipCount; ++Level)
    {
        const int32 Width = FMath::Max(1, Platform->SizeX >> Level);
        const int32 Height = FMath::Max(1, Platform->SizeY >> Level);
        const int64 Expected = static_cast<int64>((Width + 3) / 4) * ((Height + 3) / 4) * BlockBytes;
        if (Platform->Mips[Level].SizeX != Width || Platform->Mips[Level].SizeY != Height
            || !Mips.Pointers[Level] || Mips.Sizes[Level] != Expected)
            return Fail(FString::Printf(TEXT("Mip %d dimensions or byte length do not match the declared BC blocks."), Level));
        const FString Hash = Sha256(Mips.Pointers[Level], Expected);
        if (Hash.IsEmpty()) return Fail(TEXT("SHA256 provider could not hash a mip."));
        TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
        Entry->SetNumberField(TEXT("level"), Level);
        Entry->SetNumberField(TEXT("width"), Width);
        Entry->SetNumberField(TEXT("height"), Height);
        Entry->SetNumberField(TEXT("byteOffset"), Payload.Num());
        Entry->SetNumberField(TEXT("byteLength"), Expected);
        Entry->SetStringField(TEXT("sha256"), Hash);
        MipMetadata.Add(MakeShared<FJsonValueObject>(Entry));
        Payload.Append(static_cast<const uint8*>(Mips.Pointers[Level]), static_cast<int32>(Expected));
    }

    const FString PackageName = Texture->GetOutermost()->GetName();
    FString SourceFile;
    if (!FPackageName::TryConvertLongPackageNameToFilename(PackageName, SourceFile, FPackageName::GetAssetPackageExtension()))
        return Fail(TEXT("Could not resolve the source asset package file."));
    TArray<uint8> SourceBytes;
    if (!FFileHelper::LoadFileToArray(SourceBytes, *SourceFile))
        return Fail(TEXT("Could not read source asset bytes for provenance: ") + SourceFile);
    const FString SourceHash = Sha256(SourceBytes.GetData(), SourceBytes.Num());
    const FString PayloadHash = Sha256(Payload.GetData(), Payload.Num());
    if (SourceHash.IsEmpty() || PayloadHash.IsEmpty()) return Fail(TEXT("SHA256 provider could not hash the source or payload."));

    TSharedRef<FJsonObject> Header = MakeShared<FJsonObject>();
    Header->SetNumberField(TEXT("schemaVersion"), 1);
    Header->SetStringField(TEXT("name"), Texture->GetName());
    Header->SetStringField(TEXT("sourceAsset"), Texture->GetPathName());
    Header->SetStringField(TEXT("sourcePackageSha256"), SourceHash);
    Header->SetStringField(TEXT("engineVersion"), FEngineVersion::Current().ToString());
    Header->SetStringField(TEXT("sourcePlatform"), FPlatformProperties::PlatformName());
    Header->SetStringField(TEXT("format"), Format);
    Header->SetNumberField(TEXT("width"), Platform->SizeX);
    Header->SetNumberField(TEXT("height"), Platform->SizeY);
    Header->SetNumberField(TEXT("sourceWidth"), Texture->Source.GetSizeX());
    Header->SetNumberField(TEXT("sourceHeight"), Texture->Source.GetSizeY());
    Header->SetNumberField(TEXT("mipCount"), MipCount);
    Header->SetBoolField(TEXT("srgb"), Texture->SRGB != 0);
    Header->SetBoolField(TEXT("normalMap"), Platform->PixelFormat == PF_BC5 && Texture->CompressionSettings == TC_Normalmap);
    Header->SetStringField(TEXT("compositeTextureMode"), UEnum::GetValueAsString(Texture->CompositeTextureMode.GetValue()));
    Header->SetNumberField(TEXT("compositePower"), Texture->CompositePower);
    if (UTexture* Composite = Texture->CompositeTexture)
    {
        FString CompositeFile;
        TArray<uint8> CompositeBytes;
        if (!FPackageName::TryConvertLongPackageNameToFilename(Composite->GetOutermost()->GetName(), CompositeFile, FPackageName::GetAssetPackageExtension())
            || !FFileHelper::LoadFileToArray(CompositeBytes, *CompositeFile))
            return Fail(TEXT("Could not read the source composite texture package for provenance."));
        const FString CompositeHash = Sha256(CompositeBytes.GetData(), CompositeBytes.Num());
        if (CompositeHash.IsEmpty()) return Fail(TEXT("SHA256 provider could not hash the composite texture source."));
        Header->SetStringField(TEXT("compositeTexture"), Composite->GetPathName());
        Header->SetStringField(TEXT("compositePackageSha256"), CompositeHash);
    }
    Header->SetBoolField(TEXT("sourceFlipGreenApplied"), Texture->bFlipGreenChannel != 0);
    Header->SetStringField(TEXT("rowConvention"), TEXT("Unreal platform mip rows unchanged; no UV or component inversion"));
    Header->SetStringField(TEXT("addressX"), UEnum::GetValueAsString(Texture->AddressX.GetValue()));
    Header->SetStringField(TEXT("addressY"), UEnum::GetValueAsString(Texture->AddressY.GetValue()));
    Header->SetStringField(TEXT("sourceFilter"), UEnum::GetValueAsString(Texture->Filter.GetValue()));
    Header->SetStringField(TEXT("payloadSha256"), PayloadHash);
    Header->SetNumberField(TEXT("payloadBytes"), Payload.Num());
    Header->SetArrayField(TEXT("mips"), MipMetadata);
    Header->SetStringField(TEXT("limitations"), TEXT("Current Editor platform's encoded mip data, not source art or GPU readback. Dedicated export process required. No recompression, mip generation, source asset saves, row flips, or green inversion."));
    FString HeaderText;
    TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&HeaderText);
    if (!FJsonSerializer::Serialize(Header, Writer)) return Fail(TEXT("Could not serialize texture metadata."));
    FTCHARToUTF8 Utf8(*HeaderText);
    TArray<uint8> File;
    const ANSICHAR Magic[] = "MHTEX001";
    File.Append(reinterpret_cast<const uint8*>(Magic), 8);
    AppendUint32LE(File, static_cast<uint32>(Utf8.Length()));
    File.Append(reinterpret_cast<const uint8*>(Utf8.Get()), Utf8.Length());
    File.Append(Payload);
    IFileManager::Get().MakeDirectory(*FPaths::GetPath(FilePath), true);
    if (!FFileHelper::SaveArrayToFile(File, *FilePath)) return Fail(TEXT("Could not write texture interchange file: ") + FilePath);
    return FString();
}
