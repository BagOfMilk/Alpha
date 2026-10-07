using System.Collections.Generic;
using System.IO;
using Game.Gameplay.Characters;
using UnityEditor;
using UnityEngine;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// Кладе в сцену гри модульний набір персонажів (Поправка №19): об'єкт <c>CharacterKit</c> з
    /// <see cref="CharacterKitLibrary"/> (посилання на FBX з <c>Assets/Art/Characters/Kit</c>) і
    /// <see cref="CharacterPreviewRig"/> (живе прев'ю екрана створення героя). Кличе
    /// <c>GameSceneBuilder.Build</c>; посилання зберігаються в сцені, тож FBX потрапляють у білд без
    /// Resources.
    /// </summary>
    public static class CharacterKitBuilder
    {
        public const string KitFolder = "Assets/Art/Characters/Kit";

        public static GameObject Build()
        {
            var go = new GameObject("CharacterKit");
            var library = go.AddComponent<CharacterKitLibrary>();
            library.KitMale = Load("kit_m");
            library.KitFemale = Load("kit_f");
            var bodies = new List<GameObject>();
            if (Directory.Exists(KitFolder))
                foreach (var path in Directory.GetFiles(KitFolder, "body_*.fbx"))
                {
                    var body = AssetDatabase.LoadAssetAtPath<GameObject>(path.Replace('\\', '/'));
                    if (body != null) bodies.Add(body);
                }
            bodies.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            library.Bodies = bodies.ToArray();

            var preview = go.AddComponent<CharacterPreviewRig>();
            preview.Library = library;

            if (!library.IsComplete)
                Debug.LogWarning("[Kit] Набір неповний (" + KitFolder + "): екран створення лишиться на IMGUI.");
            else
                Debug.Log("[Kit] Набір персонажів: 2 набори речей, " + bodies.Count + " тіл культур.");
            return go;
        }

        private static GameObject Load(string id) => AssetDatabase.LoadAssetAtPath<GameObject>(KitFolder + "/" + id + ".fbx");
    }
}
