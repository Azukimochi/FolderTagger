using UnityEditor;

namespace FolderTagger
{
    /// <summary>
    /// Forgets the cached database lookup whenever assets change.
    /// Deliberately has no OnPreprocessTexture: Unity treats such a postprocessor as an import dependency
    /// of every texture, so adding, changing or removing it reimports all textures in the project.
    /// The bundled icons ship with their import settings already in their .meta files instead.
    /// </summary>
    class FolderTagAssetWatcher : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            // Lets the next lookup find a database that was just imported, or forget one that was deleted.
            FolderTagDatabase.ResetLookup();
        }
    }
}
