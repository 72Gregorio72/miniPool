using UnityEditor;

// The menu turns 3D ball icons so the number faces the camera: it reads the Ball mesh UVs, so the mesh must be readable.
class BallModelImport : AssetPostprocessor
{
    public override uint GetVersion() => 1;

    void OnPreprocessModel()
    {
        if (assetPath.EndsWith("Resources/Models/Ball.fbx")) ((ModelImporter)assetImporter).isReadable = true;
    }
}
