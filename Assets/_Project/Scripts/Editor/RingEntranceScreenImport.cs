using System.IO;
using Game.Core.Ring;
using UnityEditor;
using UnityEngine;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// Імпорт масок мотивів екрана виходу (ТЗ B07 п. 1): Assets/Art/Ring/Titantron/sprites/*.png — sRGB, альфа як
    /// прозорість, мипмапи (тонкі лінії й растр інакше мерехтять на екрані в 3D), розмір як є (не до степеня двійки —
    /// плитка бере ширину з пропорції PNG), плитки (tile: true у sprites.json) — Repeat, решта — Clamp; Android — ASTC.
    /// Не лінтується (TextureImporter), як і решта правил імпорту.
    /// </summary>
    public sealed class RingEntranceScreenImport : AssetPostprocessor
    {
        private const string Dir = "Assets/Art/Ring/Titantron/sprites/";

        private void OnPreprocessTexture()
        {
            string p = assetPath.Replace('\\', '/');
            if (!p.StartsWith(Dir) || !p.EndsWith(".png")) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = true;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.maxTextureSize = 1024;
            ti.filterMode = FilterMode.Trilinear;
            ti.anisoLevel = 4;
            ti.wrapMode = IsTile(Path.GetFileNameWithoutExtension(p)) ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android", overridden = true, maxTextureSize = 1024, format = TextureImporterFormat.ASTC_6x6,
            });
        }

        private static bool IsTile(string id)
        {
            try
            {
                var o = MiniJson.ParseObject(File.ReadAllText("Assets/Art/Ring/Titantron/sprites.json"));
                object v;
                if (!o.TryGetValue(id, out v)) return false;
                var d = v as System.Collections.Generic.Dictionary<string, object>;
                object t;
                return d != null && d.TryGetValue("tile", out t) && t is bool && (bool)t;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[Ринг] екран виходу: sprites.json не читається — " + ex.Message);
                return false;
            }
        }
    }
}
