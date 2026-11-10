// Original public-API interchange. No native seed generator, curve shuffler,
// position codec, or renderer implementation is reproduced here.
#include "GroomDataExportLibrary.h"
#include "GroomAsset.h"
#include "GroomBuilder.h"
#include "HairDescription.h"
#include "HairAttributes.h"
#include "DerivedDataCache.h"
#include "DerivedDataRequestOwner.h"
#include "DerivedDataRequestTypes.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/BufferArchive.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "HAL/FileManager.h"

namespace
{
TSharedRef<FJsonObject> HashObject(const FHairStrandsCommonDatas::FHash& H)
{
    auto J = MakeShared<FJsonObject>();
    J->SetStringField(TEXT("position"), LexToString(H.PositionHash));
    J->SetStringField(TEXT("curveCount"), LexToString(H.CurveCountHash));
    J->SetStringField(TEXT("curveOffset"), LexToString(H.CurveOffsetHash));
    return J;
}

struct FResourceSnapshot
{
    FString Name;
    FHairBulkContainer* Container;
    uint32 LoadedSize;
    FHairStreamingRequest::FChunk* PreviousRequest;
};

bool SaveBytes(const FString& File, const void* Data, int64 Size)
{
    if (Size < 0 || Size > MAX_int32 || (Size > 0 && !Data)) return false;
    return FFileHelper::SaveArrayToFile(TArrayView<const uint8>(static_cast<const uint8*>(Data), int32(Size)), *File);
}
}

bool UGroomDataExportLibrary::ExportBuiltGroomAttributes(const FString& AssetPath, const FString& OutputDirectory)
{
#if WITH_EDITORONLY_DATA
    using namespace UE::DerivedData;
    UGroomAsset* Groom = LoadObject<UGroomAsset>(nullptr, *AssetPath);
    if (!Groom || !IFileManager::Get().MakeDirectory(*OutputDirectory, true)) return false;
    // Output files are named after the groom asset (they used to carry one fixed character name).
    const FString OutputPrefix = Groom->GetName() + TEXT("-");
    auto Root = MakeShared<FJsonObject>();
    Root->SetStringField(TEXT("asset"), AssetPath);
    Root->SetStringField(TEXT("format"), TEXT("MHGROOM_PLATFORM_SNAPSHOT_1"));
    Root->SetBoolField(TEXT("assetsSaved"), false);
    Root->SetStringField(TEXT("implementation"), TEXT("Public platform/DDC API buffer copy; public builder called for a hash-verified source-index mapping. No seed/hash/packing algorithm copied."));
    TArray<TSharedPtr<FJsonValue>> GroupsJson;
    Root->SetNumberField(TEXT("platformGroupsAtLoad"), Groom->GetHairGroupsPlatformData().Num());
    // NullRHI deliberately skips groom PostLoad platform initialization. The
    // public CPU cache method loads the asset's normal cached strand data;
    // SetNumGroup(false,false) allocates only missing transient group containers.
    // CacheDerivedDatas/InitResources are intentionally not called.
    TArray<TSharedPtr<FJsonValue>> CacheLoads;
    if (Groom->GetHairGroupsPlatformData().Num() == 0)
    {
        const int32 GroupCount = Groom->GetHairGroupsInterpolation().Num();
        Groom->SetNumGroup(GroupCount, false, false);
        for (int32 I = 0; I < GroupCount; ++I)
        {
            FString Key;
            auto C = MakeShared<FJsonObject>(); C->SetNumberField(TEXT("groupIndex"), I);
            C->SetBoolField(TEXT("ok"), Groom->CacheStrandsData(I, Key));
            CacheLoads.Add(MakeShared<FJsonValueObject>(C));
        }
    }
    Root->SetArrayField(TEXT("explicitCpuCacheLoads"), CacheLoads);
    auto& PlatformGroups = Groom->GetHairGroupsPlatformData();
    bool bComplete = PlatformGroups.Num() > 0;
    const auto& SourceGroups = Groom->GetHairDescriptionGroups().HairGroups;
    const auto& Settings = Groom->GetHairGroupsInterpolation();
    const FHairDescription Description = Groom->GetHairDescription();
    const auto Positions = Description.VertexAttributes().GetAttributesRef<FVector3f>(HairAttribute::Vertex::Position);
    const auto Counts = Description.StrandAttributes().GetAttributesRef<int>(HairAttribute::Strand::VertexCount);
    const auto GuideFlags = Description.StrandAttributes().GetAttributesRef<int>(HairAttribute::Strand::Guide);
    FBufferArchive BuiltCurveRecords, BuiltPointRecords, BuiltPointAttributes, BuiltSourceIds;
    FBufferArchive GpuCurveRecords, GpuPointRecords, GpuPointFields;
    uint32 ExportedCurves = 0, ExportedPoints = 0;
    bool bAllMappingsAccepted = PlatformGroups.Num() > 0;
    bool bGpuFieldsComplete = PlatformGroups.Num() > 0;
    TArray<uint32> SourceOffsets;
    uint32 Offset = 0;
    for (int32 I = 0; I < Description.GetNumStrands(); ++I) { SourceOffsets.Add(Offset); Offset += Counts[FStrandID(I)]; }

    for (int32 GroupIndex = 0; GroupIndex < PlatformGroups.Num(); ++GroupIndex)
    {
        FHairStrandsBulkData& Bulk = PlatformGroups[GroupIndex].Strands.BulkData;
        const auto& H = Bulk.Header;
        auto J = MakeShared<FJsonObject>();
        J->SetNumberField(TEXT("groupIndex"), GroupIndex);
        if (SourceGroups.IsValidIndex(GroupIndex)) J->SetNumberField(TEXT("sourceGroupId"), SourceGroups[GroupIndex].Info.GroupID);
        bComplete &= H.CurveCount > 0 && H.PointCount > 0;
        J->SetNumberField(TEXT("curveCount"), H.CurveCount); J->SetNumberField(TEXT("pointCount"), H.PointCount);
        J->SetNumberField(TEXT("sourceCurveCount"), H.SourceCurveCount); J->SetNumberField(TEXT("sourcePointCount"), H.SourcePointCount);
        J->SetNumberField(TEXT("flags"), H.Flags); J->SetNumberField(TEXT("maxLengthCentimetres"), H.MaxLength); J->SetNumberField(TEXT("maxRadiusCentimetres"), H.MaxRadius);
        J->SetNumberField(TEXT("seedAttributeIndex"), HAIR_CURVE_ATTRIBUTE_SEED);
        J->SetNumberField(TEXT("seedOffsetBytesInChunk"), H.CurveAttributeOffsets[HAIR_CURVE_ATTRIBUTE_SEED]);
        J->SetNumberField(TEXT("curveAttributeChunkStride"), H.Strides.CurveAttributeChunkStride);
        J->SetNumberField(TEXT("curveAttributeChunkElementCount"), H.Strides.CurveAttributeChunkElementCount);
        J->SetNumberField(TEXT("curveStride"), H.Strides.CurveStride);
        J->SetNumberField(TEXT("curveMappingStride"), H.Strides.CurveMappingStride);
        J->SetNumberField(TEXT("pointMappingStride"), H.Strides.PointMappingStride);
        const FVector3f NativePositionOffset(Bulk.GetPositionOffset());
        TArray<TSharedPtr<FJsonValue>> PositionOffsetJson;
        PositionOffsetJson.Add(MakeShared<FJsonValueNumber>(NativePositionOffset.X)); PositionOffsetJson.Add(MakeShared<FJsonValueNumber>(NativePositionOffset.Y)); PositionOffsetJson.Add(MakeShared<FJsonValueNumber>(NativePositionOffset.Z));
        J->SetArrayField(TEXT("publicPositionOffsetCentimetres"), PositionOffsetJson);
        J->SetNumberField(TEXT("publicPackedVertexSizeBytes"), sizeof(FPackedHairVertex));
        J->SetNumberField(TEXT("publicPackedXOffsetBytes"), STRUCT_OFFSET(FPackedHairVertex, X));
        J->SetNumberField(TEXT("publicPackedYOffsetBytes"), STRUCT_OFFSET(FPackedHairVertex, Y));
        J->SetNumberField(TEXT("publicPackedZOffsetBytes"), STRUCT_OFFSET(FPackedHairVertex, Z));
        J->SetNumberField(TEXT("publicPackedUOffsetBytes"), STRUCT_OFFSET(FPackedHairVertex, UCoord));
        J->SetStringField(TEXT("publicPackedFields"), TEXT("FFloat16 X,Y,Z;uint8 UCoord;uint8 Radius:6;uint8 Type:2. Read via public C++ fields, not a copied renderer codec."));
        J->SetObjectField(TEXT("nativePrecompressionHash"), HashObject(H.Hash));
        TArray<TSharedPtr<FJsonValue>> AttributeOffsets;
        for (uint32 Value : H.CurveAttributeOffsets) AttributeOffsets.Add(MakeShared<FJsonValueNumber>(Value));
        J->SetArrayField(TEXT("curveAttributeOffsets"), AttributeOffsets);
        const FString Prefix = FString::Printf(TEXT("Group%d-"), GroupIndex);
        TArray<FResourceSnapshot> Resources;
        auto Add = [&](const TCHAR* Name, FHairBulkContainer& C) { Resources.Add({Name, &C, C.LoadedSize, C.ChunkRequest}); };
        Add(TEXT("Positions"), Bulk.Data.Positions); Add(TEXT("TranscodedPositions"), Bulk.Data.TranscodedPositions);
        Add(TEXT("CurveAttributes"), Bulk.Data.CurveAttributes); Add(TEXT("PointAttributes"), Bulk.Data.PointAttributes);
        Add(TEXT("PointToCurve"), Bulk.Data.PointToCurve); Add(TEXT("Curves"), Bulk.Data.Curves);
        Add(TEXT("CurveMapping"), Bulk.Data.CurveMapping); Add(TEXT("PointMapping"), Bulk.Data.PointMapping);

        // The public request builder supplies the actual cache keys and exact byte
        // ranges. The public cache API supplies immutable response buffers.
        FHairStreamingRequest Request;
        Request.CurveCount = H.CurveCount; Request.PointCount = H.PointCount; Request.bSupportOffsetLoad = false;
        TArray<FCacheGetChunkRequest> CacheRequests;
        Bulk.Read_DDC(&Request, CacheRequests);
        TArray<FSharedBuffer> Responses; Responses.SetNum(CacheRequests.Num());
        TArray<uint8> Success; Success.Init(0, CacheRequests.Num());
        TArray<FHairBulkContainer*> RequestedContainers;
        for (int32 I = 0; I < CacheRequests.Num(); ++I)
        {
            // UserData is explicitly caller-owned in the public DDC contract.
            const auto* Chunk = reinterpret_cast<const FHairStreamingRequest::FChunk*>(CacheRequests[I].UserData);
            RequestedContainers.Add(Chunk ? Chunk->Container : nullptr); CacheRequests[I].UserData = uint64(I);
        }
        {
            FRequestOwner Owner(EPriority::Blocking);
            GetCache().GetChunks(CacheRequests, Owner, [&](FCacheGetChunkResponse&& Response)
            {
                const int32 I = int32(Response.UserData);
                if (Responses.IsValidIndex(I) && Response.Status == EStatus::Ok) { Responses[I] = MoveTemp(Response.RawData); Success[I] = 1; }
            });
            Owner.Wait();
        }
        TArray<TSharedPtr<FJsonValue>> Buffers;
        for (const auto& R : Resources)
        {
            auto B = MakeShared<FJsonObject>(); B->SetStringField(TEXT("name"), R.Name);
            B->SetNumberField(TEXT("loadedSizeBefore"), R.LoadedSize); B->SetNumberField(TEXT("bulkSizeBefore"), R.Container->GetBulkDataSize());
            bool bSaved = false; int64 Size = 0; FString Method = TEXT("empty");
            const FString Name = Prefix + R.Name + TEXT(".bin");
            int32 I = RequestedContainers.IndexOfByKey(R.Container);
            if (I != INDEX_NONE && Success[I])
            {
                Size = Responses[I].GetSize(); Method = TEXT("public Read_DDC + GetCache.GetChunks");
                bSaved = SaveBytes(FPaths::Combine(OutputDirectory, Name), Responses[I].GetData(), Size);
            }
            else if (R.Container->GetBulkDataSize() > 0 && R.Container->IsBulkDataLoaded())
            {
                Size = R.Container->GetBulkDataSize(); Method = TEXT("loaded public FByteBulkData read-only lock");
                const void* Pointer = R.Container->Data.LockReadOnly();
                bSaved = SaveBytes(FPaths::Combine(OutputDirectory, Name), Pointer, Size); R.Container->Data.Unlock();
            }
            else if (I == INDEX_NONE) bSaved = true;
            else Method = TEXT("DDC read failed; unloaded CPU bulk not forced/rebuilt");
            B->SetBoolField(TEXT("ok"), bSaved); B->SetNumberField(TEXT("bytes"), Size); B->SetStringField(TEXT("readMethod"), Method);
            if (Size > 0) B->SetStringField(TEXT("file"), Name);
            Buffers.Add(MakeShared<FJsonValueObject>(B)); bComplete &= bSaved;
            // Restore transient stream bookkeeping; immutable asset bytes never changed.
            R.Container->LoadedSize = R.LoadedSize; R.Container->ChunkRequest = R.PreviousRequest;
        }
        J->SetArrayField(TEXT("buffers"), Buffers);

        // A rebuilt mapping is accepted only if the public builder produces the
        // identical native precompression position/count/offset hashes and counts.
        bool bHashMatch = false; uint32 VerifiedRoots = 0, FailedRoots = 0;
        if (SourceGroups.IsValidIndex(GroupIndex) && Settings.IsValidIndex(GroupIndex))
        {
            FHairStrandsDatas Built, Guides; FHairGroupInfo Info;
            FGroomBuilder::BuildData(SourceGroups[GroupIndex], Settings[GroupIndex], Info, Built, Guides, true, true, true);
            FHairStrandsBulkData RebuiltBulk;
            FGroomBuilder::BuildBulkData(Info, Built, RebuiltBulk, false);
            bHashMatch = RebuiltBulk.Header.Hash == H.Hash && Built.GetNumCurves() == H.CurveCount && Built.GetNumPoints() == H.PointCount && H.Hash.PositionHash != 0;
            J->SetObjectField(TEXT("rebuiltPrecompressionHash"), HashObject(RebuiltBulk.Header.Hash));
            FBufferArchive Mapping, Roots;
            const auto& Curves = Built.StrandsCurves;
            for (uint32 I = 0; I < Built.GetNumCurves(); ++I)
            {
                uint32 SourceId = Curves.CurvesMapping.IsValidIndex(I) ? Curves.CurvesMapping[I] : MAX_uint32;
                uint32 First = Curves.CurvesOffset[I], Count = Curves.CurvesCount[I];
                Mapping << SourceId;
                const FVector3f P = Built.StrandsPoints.PointsPosition[First];
                float X = P.X, Y = P.Y, Z = P.Z; Roots << X << Y << Z << Count << First;
                bool Valid = SourceOffsets.IsValidIndex(SourceId) && (!GuideFlags.IsValid() || GuideFlags[FStrandID(SourceId)] == 0);
                if (Valid) Valid = (P - Positions[FVertexID(SourceOffsets[SourceId])]).Size() < 0.00001f;
                if (Valid) ++VerifiedRoots; else ++FailedRoots;
            }
            const FString MappingName = Prefix + TEXT("RebuiltOriginalCurveIds.uint32.bin");
            const FString RootsName = Prefix + TEXT("RebuiltRootCountOffset.xyz32-u32-u32.bin");
            bComplete &= FFileHelper::SaveArrayToFile(Mapping, *FPaths::Combine(OutputDirectory, MappingName));
            bComplete &= FFileHelper::SaveArrayToFile(Roots, *FPaths::Combine(OutputDirectory, RootsName));
            J->SetStringField(TEXT("rebuiltCurveSourceIds"), MappingName); J->SetStringField(TEXT("rebuiltRootsCountOffset"), RootsName);
            // New interchange dataset, in the native built curve/point order.
            // Public CPU builder values are exported directly, without decoding
            // native GPU position packing or reimplementing decimation.
            const uint32 PointBase = ExportedPoints;
            const int32 SourceGroupId = SourceGroups[GroupIndex].Info.GroupID;
            float MinimumRadius = MAX_flt, MaximumRadius = 0;
            for (uint32 I = 0; I < Built.GetNumCurves(); ++I)
            {
                int32 First = int32(PointBase + Curves.CurvesOffset[I]), Count = Curves.CurvesCount[I], Guide = 0, Group = SourceGroupId;
                const FVector2f UV = Curves.CurvesRootUV.IsValidIndex(I) ? Curves.CurvesRootUV[I] : FVector2f::ZeroVector;
                float U = UV.X, V = UV.Y, Diameter = Built.StrandsPoints.PointsRadius[Curves.CurvesOffset[I]] * 2;
                BuiltCurveRecords << First << Count << Guide << Group << U << V << Diameter;
                uint32 SourceId = Curves.CurvesMapping.IsValidIndex(I) ? Curves.CurvesMapping[I] : MAX_uint32;
                BuiltSourceIds << SourceId;
            }
            for (uint32 I = 0; I < Built.GetNumPoints(); ++I)
            {
                const FVector3f P = Built.StrandsPoints.PointsPosition[I];
                float X = P.X, Y = P.Y, Z = P.Z, Radius = Built.StrandsPoints.PointsRadius[I], Diameter = Radius * 2;
                float U = Built.StrandsPoints.PointsCoordU.IsValidIndex(I) ? Built.StrandsPoints.PointsCoordU[I] : -1;
                uint32 SourcePoint = Built.StrandsPoints.PointsMapping.IsValidIndex(I) ? Built.StrandsPoints.PointsMapping[I] : MAX_uint32;
                BuiltPointRecords << X << Y << Z << Diameter;
                BuiltPointAttributes << U << Radius << SourcePoint;
                MinimumRadius = FMath::Min(MinimumRadius, Radius); MaximumRadius = FMath::Max(MaximumRadius, Radius);
            }
            J->SetNumberField(TEXT("nativeBuiltMergedCurveOffset"), ExportedCurves); J->SetNumberField(TEXT("nativeBuiltMergedPointOffset"), ExportedPoints);
            J->SetNumberField(TEXT("builtPhysicalRadiusMinimumCentimetres"), MinimumRadius); J->SetNumberField(TEXT("builtPhysicalRadiusMaximumCentimetres"), MaximumRadius);
            J->SetBoolField(TEXT("hasExtraEndingPoint"), Bulk.HasFlags(FHairStrandsBulkData::DataFlags_HasExtraEndingPoint));
            TArray<uint8> PositionBytes;
            const bool bPlainPackedPositions = !Bulk.HasFlags(FHairStrandsBulkData::DataFlags_HasTranscodedPosition)
                && FFileHelper::LoadFileToArray(PositionBytes, *FPaths::Combine(OutputDirectory, Prefix + TEXT("Positions.bin")))
                && PositionBytes.Num() == int64(Built.GetNumPoints()) * sizeof(FPackedHairVertex);
            J->SetBoolField(TEXT("publicPackedPositionsDecoded"), bPlainPackedPositions);
            bGpuFieldsComplete &= bPlainPackedPositions;
            if (bPlainPackedPositions)
            {
                auto PackedAt = [&PositionBytes](uint32 I)
                {
                    FPackedHairVertex P;
                    FMemory::Memcpy(&P, PositionBytes.GetData() + uint64(I) * sizeof(P), sizeof(P));
                    return P;
                };
                for (uint32 I = 0; I < Built.GetNumCurves(); ++I)
                {
                    int32 First = int32(PointBase + Curves.CurvesOffset[I]), Count = Curves.CurvesCount[I], Guide = 0, Group = SourceGroupId;
                    const FVector2f UV = Curves.CurvesRootUV.IsValidIndex(I) ? Curves.CurvesRootUV[I] : FVector2f::ZeroVector;
                    const FPackedHairVertex P = PackedAt(Curves.CurvesOffset[I]);
                    float U = UV.X, V = UV.Y, Diameter = (float(P.Radius) / 63.f) * H.MaxRadius * 2;
                    GpuCurveRecords << First << Count << Guide << Group << U << V << Diameter;
                }
                for (uint32 I = 0; I < Built.GetNumPoints(); ++I)
                {
                    const FPackedHairVertex P = PackedAt(I);
                    const FVector3f Position = FVector3f(P.X.GetFloat(), P.Y.GetFloat(), P.Z.GetFloat()) + NativePositionOffset;
                    float X = Position.X, Y = Position.Y, Z = Position.Z;
                    uint8 Radius6 = P.Radius, Type = P.Type, UByte = P.UCoord, Reserved = 0;
                    float U = float(UByte) / 255.f, Radius = (float(Radius6) / 63.f) * H.MaxRadius, Diameter = Radius * 2;
                    GpuPointRecords << X << Y << Z << Diameter;
                    GpuPointFields << U << Radius << Radius6 << Type << UByte << Reserved;
                }
            }
            ExportedCurves += Built.GetNumCurves(); ExportedPoints += Built.GetNumPoints();
        }
        J->SetBoolField(TEXT("nativeAndRebuiltHashMatch"), bHashMatch);
        J->SetNumberField(TEXT("sourceRootMappingsVerified"), VerifiedRoots); J->SetNumberField(TEXT("sourceRootMappingsFailed"), FailedRoots);
        J->SetBoolField(TEXT("sourceMappingAccepted"), bHashMatch && VerifiedRoots == H.CurveCount && FailedRoots == 0);
        bAllMappingsAccepted &= bHashMatch && VerifiedRoots == H.CurveCount && FailedRoots == 0;
        GroupsJson.Add(MakeShared<FJsonValueObject>(J));
    }
    Root->SetArrayField(TEXT("groups"), GroupsJson); Root->SetBoolField(TEXT("allBuffersExported"), bComplete);
    if (bAllMappingsAccepted)
    {
        FBufferArchive BuiltFile;
        const ANSICHAR Magic[] = "MHGRM001"; BuiltFile.Serialize((void*)Magic, 8);
        BuiltFile << ExportedCurves << ExportedPoints;
        BuiltFile.Serialize(BuiltCurveRecords.GetData(), BuiltCurveRecords.Num()); BuiltFile.Serialize(BuiltPointRecords.GetData(), BuiltPointRecords.Num());
        bComplete &= FFileHelper::SaveArrayToFile(BuiltFile, *FPaths::Combine(OutputDirectory, OutputPrefix + TEXT("NativeBuilt.mhgroom")));
        bComplete &= FFileHelper::SaveArrayToFile(BuiltSourceIds, *FPaths::Combine(OutputDirectory, OutputPrefix + TEXT("NativeBuiltOriginalCurveIds.uint32.bin")));
        bComplete &= FFileHelper::SaveArrayToFile(BuiltPointAttributes, *FPaths::Combine(OutputDirectory, OutputPrefix + TEXT("NativeBuiltPointData.u-radius-groupPoint.bin")));
        Root->SetStringField(TEXT("nativeBuiltGroom"), OutputPrefix + TEXT("NativeBuilt.mhgroom"));
        Root->SetStringField(TEXT("nativeBuiltOriginalCurveIds"), OutputPrefix + TEXT("NativeBuiltOriginalCurveIds.uint32.bin"));
        Root->SetStringField(TEXT("nativeBuiltPointData"), OutputPrefix + TEXT("NativeBuiltPointData.u-radius-groupPoint.bin"));
        Root->SetStringField(TEXT("nativeBuiltPointDataFormat"), TEXT("Per built point:float32 normalized arc U,float32 physical radius centimetres,uint32 original non-guide point index within its source group. Includes authored helper ending points; mapped source indices may repeat."));
    }
    Root->SetBoolField(TEXT("allSourceMappingsAccepted"), bAllMappingsAccepted);
    if (bAllMappingsAccepted && bGpuFieldsComplete)
    {
        FBufferArchive GpuFile;
        const ANSICHAR Magic[] = "MHGRM001"; GpuFile.Serialize((void*)Magic, 8);
        GpuFile << ExportedCurves << ExportedPoints;
        GpuFile.Serialize(GpuCurveRecords.GetData(), GpuCurveRecords.Num()); GpuFile.Serialize(GpuPointRecords.GetData(), GpuPointRecords.Num());
        bComplete &= FFileHelper::SaveArrayToFile(GpuFile, *FPaths::Combine(OutputDirectory, OutputPrefix + TEXT("NativePacked.mhgroom")));
        bComplete &= FFileHelper::SaveArrayToFile(GpuPointFields, *FPaths::Combine(OutputDirectory, OutputPrefix + TEXT("NativePackedPointFields.bin")));
        Root->SetStringField(TEXT("nativePackedGroom"), OutputPrefix + TEXT("NativePacked.mhgroom"));
        Root->SetStringField(TEXT("nativePackedPointFields"), OutputPrefix + TEXT("NativePackedPointFields.bin"));
        Root->SetStringField(TEXT("nativePackedPointFieldsFormat"), TEXT("Per point:float32 UByte/255,float32 Radius6/63*Header.MaxRadiusCm,uint8 Radius6,uint8 Type,uint8 UByte,uint8 reserved0. Position=public FFloat16 fields.GetFloat()+public bulk.GetPositionOffset() cast to FVector3f. No position or radius packing/generator code copied."));
    }
    Root->SetBoolField(TEXT("allPublicGpuFieldsDecoded"), bGpuFieldsComplete);
    Root->SetBoolField(TEXT("allBuffersExported"), bComplete);
    FString Json; FJsonSerializer::Serialize(Root, TJsonWriterFactory<>::Create(&Json));
    return FFileHelper::SaveStringToFile(Json, *FPaths::Combine(OutputDirectory, TEXT("PlatformGroomAttributes.json"))) && bComplete;
#else
    return false;
#endif
}
