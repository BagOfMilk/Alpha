using UnityEditor;
using UnityEngine;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// Імпорт звуку меню «Рингу» (ТЗ B08 §5, Assets/Art/Ring/Audio/**): музика — стерео, Vorbis, потокова (не розпаковується в
    /// пам'ять цілком), петля без зсуву; звуки меню — короткі, ADPCM, розпаковуються при завантаженні (без затримки на клік).
    /// Джерело — WAV 48 кГц з tools/ring_audio; Unity сам стискає. Не лінтується (AudioImporter — глибокий API редактора).
    /// </summary>
    public sealed class RingMenuAudioImport : AssetPostprocessor
    {
        private const string Root = "Assets/Art/Ring/Audio/";

        private void OnPreprocessAudio()
        {
            if (!assetPath.Replace('\\', '/').StartsWith(Root)) return;
            var imp = (AudioImporter)assetImporter;
            bool music = System.IO.Path.GetFileName(assetPath).StartsWith("music_");
            var s = imp.defaultSampleSettings;
            if (music)
            {
                imp.forceToMono = false;
                imp.loadInBackground = true;
                s.loadType = AudioClipLoadType.Streaming;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.7f;
            }
            else
            {
                imp.forceToMono = false;
                imp.loadInBackground = false;
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.ADPCM;
            }
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            s.preloadAudioData = !music;
            imp.defaultSampleSettings = s;
        }
    }
}
