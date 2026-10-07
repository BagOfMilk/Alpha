using System.Collections.Generic;
using System.IO;
using Game.Gameplay.Audio;
using UnityEditor;
using UnityEngine;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// Кладе в сцену гри звук треку V7 (Поправка №18.4): об'єкт <c>Audio</c> з <see cref="AudioLibrary"/>
    /// (кліпи з <c>Assets/ThirdParty/CC0/Audio</c> за ключами <c>audio_manifest.txt</c>) і
    /// <see cref="AudioDirector"/>. Кличе <c>GameSceneBuilder.Build</c>.
    /// </summary>
    public static class AudioLibraryBuilder
    {
        public const string Root = "Assets/ThirdParty/CC0/Audio/";

        public static GameObject Build()
        {
            var go = new GameObject("Audio");
            go.AddComponent<AudioListener>(); // у сцені його не було: камери створюються кодом без слухача
            var library = go.AddComponent<AudioLibrary>();
            var keys = new List<string>();
            var clips = new List<AudioClip>();
            string manifest = Root + "audio_manifest.txt";
            if (File.Exists(manifest))
                foreach (var line in File.ReadAllLines(manifest))
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    AudioClip clip = null;
                    foreach (var ext in new[] { ".ogg", ".wav", ".mp3" })
                        if ((clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + line + ext)) != null) break;
                    if (clip == null) { Debug.LogWarning("[Звук] немає кліпу " + line); continue; }
                    keys.Add(line);
                    clips.Add(clip);
                }
            library.Keys = keys.ToArray();
            library.Clips = clips.ToArray();
            go.AddComponent<AudioDirector>();
            Debug.Log("[Звук] кліпів: " + clips.Count);
            return go;
        }
    }

    /// <summary>Імпорт звуку: музика й атмосфера — потоком (Vorbis), короткі такти — розпаковані в пам'яті.</summary>
    public sealed class AudioImportSettings : AssetPostprocessor
    {
        private void OnPreprocessAudio()
        {
            if (assetPath == null || !assetPath.StartsWith(AudioLibraryBuilder.Root)) return;
            var importer = (AudioImporter)assetImporter;
            bool longClip = assetPath.Contains("/Music/") || assetPath.Contains("/Ambience/");
            var s = importer.defaultSampleSettings;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = longClip ? 0.6f : 0.7f;
            s.loadType = longClip ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            importer.defaultSampleSettings = s;
            importer.forceToMono = !longClip;
            importer.loadInBackground = longClip;
        }
    }
}
