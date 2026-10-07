using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// Правила імпорту власних асетів треку V (Поправка №18/№19) — усе під <c>Assets/Art</c>,
    /// згенероване в Blender (<c>tools/blender/*.py</c>). Скриптом, а не в інспекторі, з тієї ж
    /// причини, що <see cref="KenneyImportSettings"/>: файлів сотні, і правило має повторюватись на
    /// будь-якій машині.
    ///
    /// Головне:
    /// 1. Матеріали — <c>ImportViaMaterialDescription</c> (граблі Kenney: підмінений шейдер губить колір).
    /// 2. Персонажі й кліпи — Humanoid (ретаргет UAL на тіла набору), БЕЗ оптимізації ієрархії: збирач
    ///    (<c>CharacterAssembler</c>) переносить речі на скелет тіла за іменами кісток, тож кістки
    ///    мусять лишатися об'єктами.
    /// 3. Волосся, бороди, пір'я — альфа-зріз і дві сторони (картки волосся тонкі, прозорість
    ///    сортувалася б абияк). Розпізнається за іменем: <c>hairN_*</c>, <c>*_hair</c>, beard, moustache, feather.
    /// 4. Текстури: <c>*_nor*</c>/<c>*NORMAL</c> — карти нормалей, шорсткість і блиск — лінійні; не більше 2048.
    /// </summary>
    public sealed class ArtImportSettings : AssetPostprocessor
    {
        private const string Root = "Assets/Art/";
        private const string Characters = Root + "Characters/";
        private const string Animations = Root + "Animations/";

        private bool IsArt => assetPath != null && assetPath.StartsWith(Root);

        /// <summary>Після вбудованого препроцесора URP — щоб наші правки матеріалу були останніми.</summary>
        public override int GetPostprocessOrder() => 100;

        /// <summary>
        /// Версія правил: зміна змушує Unity переімпортувати всі асети, яких вони стосуються.
        /// 2 — текстура за назвою матеріалу; 3 — матовість; 4 — очі MPFB (07.10.2026).
        /// </summary>
        public override uint GetVersion() => 4;

        private static readonly string[] TextureFolders = { "Assets/Art/Textures/", "Assets/Art/Textures/Kit/" };
        private static readonly string[] DiffuseSuffixes = { "", "_Diffuse", "_diff", "_col_01", "_COL", "_albedo" };
        private static readonly string[] NormalSuffixes = { "_nor_gl", "_nor", "_normal" };
        private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg" };

        /// <summary>
        /// Текстура за назвою матеріалу, коли FBX її не приніс. Дві причини з першого запуску (07.10.2026):
        /// Blender пише в FBX ім'я файлу з суфіксом роздільності («stone_wall_04_Diffuse_1k.jpg»), а на диску —
        /// без нього; і шкіра MPFB іде до кольору через вузли, тож експортер не прив'язує її до DiffuseColor.
        /// </summary>
        private Texture2D FindByMaterialName(string materialName, string[] suffixes)
        {
            if (string.IsNullOrEmpty(materialName)) return null;
            string name = materialName;
            int dot = name.IndexOf('.');
            if (dot > 0) name = name.Substring(0, dot); // «stone_wall_04.001» → «stone_wall_04»
            foreach (var folder in TextureFolders)
                foreach (var suffix in suffixes)
                    foreach (var ext in Extensions)
                    {
                        string path = folder + name + suffix + ext;
                        context.DependsOnSourceAsset(path);
                        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                        if (tex != null) return tex;
                    }
            return null;
        }

        private void OnPreprocessModel()
        {
            if (!IsArt) return;
            var importer = (ModelImporter)assetImporter;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.importBlendShapes = false;

            if (assetPath.StartsWith(Characters))
            {
                // Humanoid: кліпи UAL (інший скелет) ретаргетуються на тіла набору. Ієрархія не оптимізується —
                // збирач переносить речі на кістки за іменами.
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = false;
                importer.optimizeGameObjects = false;
            }
            else if (assetPath.StartsWith(Animations))
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = true;
                importer.optimizeGameObjects = false;
            }
            else
            {
                // Будівлі, реквізит, земля — статичні.
                importer.animationType = ModelImporterAnimationType.None;
                importer.importAnimation = false;
            }
        }

        /// <summary>
        /// Кліпи UAL: ім'я без префікса дубля («Rig|Rig|Idle_Loop» → «Idle_Loop»), петля для «*_Loop» і
        /// робочих циклів, корінь запечений у позу (рух веде гра, не кліп).
        /// </summary>
        private void OnPreprocessAnimation()
        {
            if (assetPath == null || !assetPath.StartsWith(Animations)) return;
            var importer = (ModelImporter)assetImporter;
            var clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;
            foreach (var c in clips)
            {
                c.name = Game.Gameplay.UI.AnimStateTable.NormalizeClipName(c.takeName);
                bool loop = c.name.EndsWith("_Loop") || c.name == "Farm_Harvest" || c.name == "Fixing_Kneeling"
                            || c.name == "Interact" || c.name == "Sword_Idle" || c.name.StartsWith("Pistol_Aim");
                c.loopTime = loop;
                c.lockRootRotation = true;
                c.lockRootHeightY = true;
                c.lockRootPositionXZ = true;
                c.keepOriginalOrientation = true;
                c.keepOriginalPositionY = true;
                c.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
        }

        private void OnPreprocessTexture()
        {
            if (!IsArt) return;
            var importer = (TextureImporter)assetImporter;
            string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            string lower = file.ToLowerInvariant();
            importer.maxTextureSize = 2048;
            importer.mipmapEnabled = true;

            bool hairDiffuse = lower.StartsWith("hairn_"); // нейтральна текстура волосся, не нормаль
            if (!hairDiffuse && (lower.Contains("_nor") || lower.EndsWith("normal")))
            {
                importer.textureType = TextureImporterType.NormalMap;
                return;
            }
            importer.textureType = TextureImporterType.Default;
            if (lower.Contains("_rough") || lower.Contains("_spec") || lower.Contains("_metal") || lower.Contains("_ao"))
                importer.sRGBTexture = false;
            importer.alphaIsTransparency = hairDiffuse || lower.EndsWith("_hair") || lower.Contains("feather");
        }

        private void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips)
        {
            if (!IsArt || material == null) return;

            // Колір дає текстура, а відтінок тканини — гра (_BaseColor через MaterialPropertyBlock).
            // Blender пише в FBX множник 0.8 — без цього все було б на п'яту частину темнішим.
            TexturePropertyDescription diffuse;
            bool hasDiffuse = description.TryGetProperty("DiffuseColor", out diffuse) && diffuse.texture != null;
            if (!hasDiffuse && material.HasProperty("_BaseMap"))
            {
                var tex = FindByMaterialName(description.materialName, DiffuseSuffixes);
                // Очі MPFB: матеріал «Human.low-poly.NNN», текстура йде через вузли — беремо карі очі набору.
                if (tex == null && (description.materialName ?? "").ToLowerInvariant().Contains("low-poly"))
                    tex = FindByMaterialName("brown_eye", DiffuseSuffixes);
                if (tex != null) { material.SetTexture("_BaseMap", tex); hasDiffuse = true; }
            }
            if (hasDiffuse && material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", Color.white);

            // Матовість: Blender пише в FBX блиск ~0,5, а в селі без зонда відбиттів гладка поверхня
            // віддзеркалює темне небо — постаті й тканини виходили темними (тур 07.10.2026). Неметалам —
            // не більше 0,25; метал (кольчуга, зброя, обручі) лишається блискучим.
            bool metal = material.HasProperty("_Metallic") && material.GetFloat("_Metallic") >= 0.5f;
            if (!metal && material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", Mathf.Min(material.GetFloat("_Smoothness"), 0.25f));
            if (material.HasProperty("_BumpMap") && material.GetTexture("_BumpMap") == null)
            {
                var nor = FindByMaterialName(description.materialName, NormalSuffixes);
                if (nor != null)
                {
                    material.SetTexture("_BumpMap", nor);
                    material.EnableKeyword("_NORMALMAP");
                }
            }

            if (!NeedsCutout(description, diffuse)) return;
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 0f);
            if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 1f);
            if (material.HasProperty("_Cutoff")) material.SetFloat("_Cutoff", 0.4f);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)CullMode.Off);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 1f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.One);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.Zero);
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_ALPHATEST_ON");
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.renderQueue = (int)RenderQueue.AlphaTest;
            material.doubleSidedGI = true;
        }

        private static bool NeedsCutout(MaterialDescription description, TexturePropertyDescription diffuse)
        {
            string name = (description.materialName ?? string.Empty).ToLowerInvariant();
            string tex = diffuse.path != null ? System.IO.Path.GetFileNameWithoutExtension(diffuse.path).ToLowerInvariant() : string.Empty;
            foreach (var s in new[] { name, tex })
                if (s.StartsWith("hairn_") || s.EndsWith("_hair") || s.Contains("beard") || s.Contains("moustache") || s.Contains("feather"))
                    return true;
            return false;
        }
    }
}
