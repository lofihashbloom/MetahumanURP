using UnityEngine;

// v34 teaser: binds a head-local hair extinction volume (built once from the groom strands, which are rigid on the
// head bone) to the character's strand materials. Every frame the world->volume transform follows the head, so the
// per-light ray march in HairStrands stays correct under head animation and for any light (e.g. a back light).
[ExecuteAlways, DefaultExecutionOrder(2000)]
public sealed class HairDensityBinder : MonoBehaviour
{
    public Transform head;
    public Texture3D density;
    public Vector3 boundsMin, boundsSize;      // head-local metres
    [Min(0)] public float densityScale = .7f;   // same projected-area scale as the earlier static volumes
    public Material[] materials = new Material[0];
    public Vector3 skullCentre, skullRadii;     // head-local ellipsoid (radii 0 = none): the scattering march stops there

    static readonly int VolumeId = Shader.PropertyToID("_HairDensity"), MatrixId = Shader.PropertyToID("_HairDensityWorldToVolume"),
        SizeId = Shader.PropertyToID("_HairDensitySize"), ScaleId = Shader.PropertyToID("_HairDensityScale"),
        EnabledId = Shader.PropertyToID("_HairDensityEnabled"), SkullCentreId = Shader.PropertyToID("_HairSkullCentre"),
        SkullRadiiId = Shader.PropertyToID("_HairSkullRadii");

    public void Apply()
    {
        if (!head || !density) return;
        var toVolume = Matrix4x4.Scale(new Vector3(1 / boundsSize.x, 1 / boundsSize.y, 1 / boundsSize.z)) *
                       Matrix4x4.Translate(-boundsMin) * head.worldToLocalMatrix;
        float voxel = Mathf.Max(boundsSize.x, Mathf.Max(boundsSize.y, boundsSize.z)) / Mathf.Max(density.width, 1);
        foreach (var m in materials)
        {
            if (!m) continue;
            m.SetTexture(VolumeId, density); m.SetMatrix(MatrixId, toVolume);
            m.SetVector(SizeId, new Vector4(boundsSize.x, boundsSize.y, boundsSize.z, voxel));
            m.SetFloat(ScaleId, densityScale); m.SetFloat(EnabledId, 1);
            // Skull in volume coordinates (same affine map as the density: per-axis scale of the cube bounds).
            var c = new Vector3((skullCentre.x - boundsMin.x) / boundsSize.x, (skullCentre.y - boundsMin.y) / boundsSize.y, (skullCentre.z - boundsMin.z) / boundsSize.z);
            m.SetVector(SkullCentreId, c);
            m.SetVector(SkullRadiiId, new Vector4(skullRadii.x / boundsSize.x, skullRadii.y / boundsSize.y, skullRadii.z / boundsSize.z, skullRadii.x > 0 ? 1 : 0));
        }
    }

    void OnEnable() => Apply();
    void LateUpdate() => Apply();
}
