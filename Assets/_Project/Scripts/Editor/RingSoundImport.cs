using UnityEditor;
using UnityEngine;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// Імпорт звуку РИНГУ (пакет П4 до М1, ПМ 08.11.2026: «звук рингу імпортувати моно»): набір сцени
    /// <c>Assets/ThirdParty/CC0/Audio/Ring/</c> і нові звуки подій <c>Assets/Art/Ring/Sound/</c> — моно (звук двовимірний, камера
    /// кадрує ринг) і Vorbis. Пам'ять: музика виходів — потокова (не розпаковується цілком), гул залу й оплески — стиснені в пам'яті,
    /// короткі удари — розпаковані при завантаженні (без затримки на контакт). Звук меню (<c>Assets/Art/Ring/Audio/</c>) — у
    /// <see cref="RingMenuAudioImport"/>, тут не чіпається. Іде після загального правила Alpha (<c>AudioImportSettings</c>, тека CC0/Audio)
    /// і перекриває його для рингу. Значення .meta звіряє <c>RingSoundImportTests</c>. Не лінтується (AudioImporter — API редактора).
    /// </summary>
    public sealed class RingSoundImport : AssetPostprocessor
    {
        public static readonly string[] Roots = { "Assets/ThirdParty/CC0/Audio/Ring/", "Assets/Art/Ring/Sound/" };

        public override int GetPostprocessOrder() { return 100; }

        public override uint GetVersion() { return 1; }

        public static bool IsRing(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            path = path.Replace('\\', '/');
            for (int i = 0; i < Roots.Length; i++)
                if (path.StartsWith(Roots[i])) return true;
            return false;
        }

        private void OnPreprocessAudio()
        {
            if (!IsRing(assetPath)) return;
            var imp = (AudioImporter)assetImporter;
            string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            bool music = name.StartsWith("music_");
            bool bed = name == "crowd_shouting" || name == "applause_hall";
            imp.forceToMono = true;
            imp.loadInBackground = music;
            var s = imp.defaultSampleSettings;
            s.loadType = music ? AudioClipLoadType.Streaming : bed ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = music ? 0.6f : 0.7f;
            s.preloadAudioData = !music;
            imp.defaultSampleSettings = s;
        }
    }
}
