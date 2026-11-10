#include "GroomDataExportLibrary.h"
#include "GroomAsset.h"
#include "GroomComponent.h"
#include "HairDescription.h"
#include "HairAttributes.h"
#include "Misc/FileHelper.h"
#include "Serialization/BufferArchive.h"
#include "Modules/ModuleManager.h"

IMPLEMENT_MODULE(FDefaultModuleImpl, GroomExportBridge)

bool UGroomDataExportLibrary::ExportGroomData(const FString& AssetPath, const FString& FilePath)
{
    const UGroomAsset* Groom = LoadObject<UGroomAsset>(nullptr, *AssetPath);
    if (!Groom) return false;
    const FHairDescription Description = Groom->GetHairDescription();
    if (!Description.IsValid()) return false;
    const auto Positions = Description.VertexAttributes().GetAttributesRef<FVector3f>(HairAttribute::Vertex::Position);
    const auto Counts = Description.StrandAttributes().GetAttributesRef<int>(HairAttribute::Strand::VertexCount);
    const auto Guides = Description.StrandAttributes().GetAttributesRef<int>(HairAttribute::Strand::Guide);
    const auto Groups = Description.StrandAttributes().GetAttributesRef<int>(HairAttribute::Strand::GroupID);
    const auto UVs = Description.StrandAttributes().GetAttributesRef<FVector2f>(HairAttribute::Strand::RootUV);
    const auto StrandWidths = Description.StrandAttributes().GetAttributesRef<float>(HairAttribute::Strand::Width);
    const auto VertexWidths = Description.VertexAttributes().GetAttributesRef<float>(HairAttribute::Vertex::Width);
    const auto GroomWidths = Description.GroomAttributes().GetAttributesRef<float>(HairAttribute::Groom::Width);
    if (!Positions.IsValid() || !Counts.IsValid()) return false;
    FBufferArchive Bytes;
    const ANSICHAR Magic[] = "MHGRM001";
    Bytes.Serialize((void*)Magic, 8);
    int32 StrandCount = Description.GetNumStrands(), VertexCount = Description.GetNumVertices();
    Bytes << StrandCount << VertexCount;
    int32 First = 0;
    for (int32 S = 0; S < StrandCount; ++S)
    {
        const FStrandID Id(S);
        int32 Count = Counts[Id], Guide = Guides.IsValid() ? Guides[Id] : 0, Group = Groups.IsValid() ? Groups[Id] : 0;
        if (Count < 1 || First + Count > VertexCount) return false;
        const FVector2f UV = UVs.IsValid() ? UVs[Id] : FVector2f::ZeroVector;
        float U = UV.X, V = UV.Y;
        float Width = StrandWidths.IsValid() ? StrandWidths[Id] : (GroomWidths.IsValid() ? GroomWidths[FGroomID(0)] : 0.f);
        Bytes << First << Count << Guide << Group << U << V << Width;
        First += Count;
    }
    if (First != VertexCount) return false;
    for (int32 V = 0; V < VertexCount; ++V)
    {
        const FVertexID Id(V);
        const FVector3f Position = Positions[Id];
        float X = Position.X, Y = Position.Y, Z = Position.Z;
        float Width = VertexWidths.IsValid() ? VertexWidths[Id] : 0.f;
        Bytes << X << Y << Z << Width;
    }
    return FFileHelper::SaveArrayToFile(Bytes, *FilePath);
}

void UGroomDataExportLibrary::ConfigureReferenceGroom(UGroomComponent* Component)
{
    if (!Component) return;
    Component->SetForcedLOD(0);
    Component->SetUseCards(false);
    Component->SetEnableSimulation(false);
}
