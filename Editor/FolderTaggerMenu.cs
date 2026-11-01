using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FolderTagger
{
    /// <summary>Folder Tagger entries in the Assets menu, which is also the Project window's context menu.</summary>
    static class FolderTaggerMenu
    {
        const string Root = "Assets/Folder Tagger/";
        const int Priority = 2000;

        [MenuItem(Root + "タグを割り当て…", false, Priority)]
        static void AssignTag()
        {
            var guids = GetSelectedFolderGuids();
            var position = FolderTagDrawer.LastClickScreenPosition;
            if (position == Vector2.zero && EditorWindow.focusedWindow != null)
                position = EditorWindow.focusedWindow.position.center;
            // Open once the context menu has closed, otherwise the dropdown loses focus and closes immediately.
            EditorApplication.delayCall += () => FolderTagPicker.Show(guids, position);
        }

        [MenuItem(Root + "タグを割り当て…", true)]
        static bool ValidateAssignTag() => GetSelectedFolderGuids().Length > 0;

        [MenuItem(Root + "タグを外す", false, Priority + 1)]
        static void ClearTag()
        {
            var db = FolderTagDatabase.Find();
            if (db != null) db.Assign(GetSelectedFolderGuids(), null);
        }

        [MenuItem(Root + "タグを外す", true)]
        static bool ValidateClearTag()
        {
            var db = FolderTagDatabase.Find();
            return db != null && GetSelectedFolderGuids().Any(g => db.GetTagForFolder(g) != null);
        }

        [MenuItem(Root + "設定を開く", false, Priority + 20)]
        static void OpenSettings() => FolderTaggerWindow.Open();

        internal static string[] GetSelectedFolderGuids() =>
            Selection.assetGUIDs.Where(g => AssetDatabase.IsValidFolder(AssetDatabase.GUIDToAssetPath(g))).ToArray();
    }
}
