using MetaHuman.Groom;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MetaHuman.Editor.Groom
{
    [InitializeOnLoad]
    static class CardSurfaceSaveHook
    {
        static CardSurfaceSaveHook()
        {
            // Editor previews own transient mesh clones. Persist only the generated rest assets.
            EditorSceneManager.sceneSaving += (scene, path) => {
                foreach (var deformer in Object.FindObjectsByType<CardSurfaceDeformer>(FindObjectsInactive.Include))
                    if (deformer.gameObject.scene == scene) deformer.Release();
            };
            PrefabStage.prefabSaving += root => {
                foreach (var deformer in root.GetComponentsInChildren<CardSurfaceDeformer>(true)) deformer.Release();
            };
        }
    }
}
