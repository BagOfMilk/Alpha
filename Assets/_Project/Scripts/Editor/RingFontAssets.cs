using Game.Gameplay.Ring.UI;
using UnityEditor;
using UnityEngine;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// Збирає асет набору шрифтів «Рингу» (<c>Assets/Resources/Ring/RingFontSet.asset</c>) з TTF у
    /// <c>Assets/ThirdParty/Fonts/</c> (OFL; ТЗ B00 §2). Ідемпотентно; кличе збирач сцени перед білдом.
    /// </summary>
    public static class RingFontAssets
    {
        private const string AssetPath = "Assets/Resources/Ring/RingFontSet.asset";
        private const string Fonts = "Assets/ThirdParty/Fonts/";

        [MenuItem("Ринг/Зібрати набір шрифтів")]
        public static void Ensure()
        {
            var set = AssetDatabase.LoadAssetAtPath<RingFontSet>(AssetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<RingFontSet>();
                AssetDatabase.CreateAsset(set, AssetPath);
            }
            set.Text = Load("Fixel/FixelText-Regular.ttf");
            set.TextBold = Load("Fixel/FixelText-SemiBold.ttf");
            set.Display700 = Load("SofiaSansExtraCondensed/SofiaSansExtraCondensed-Bold.ttf");
            set.Display800 = Load("SofiaSansExtraCondensed/SofiaSansExtraCondensed-ExtraBold.ttf");
            set.Display900 = Load("SofiaSansExtraCondensed/SofiaSansExtraCondensed-Black.ttf");
            set.Hand = Load("AmaticSC/AmaticSC-Bold.ttf");
            set.Spray = Load("RubikSprayPaint/RubikSprayPaint-Regular.ttf");
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
        }

        private static Font Load(string rel)
        {
            var f = AssetDatabase.LoadAssetAtPath<Font>(Fonts + rel);
            if (f == null) Debug.LogWarning("[Ринг] шрифт не знайдено: " + Fonts + rel);
            return f;
        }
    }
}
