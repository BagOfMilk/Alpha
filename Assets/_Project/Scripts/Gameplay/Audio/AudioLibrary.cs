using System.Collections.Generic;
using UnityEngine;

namespace Game.Gameplay.Audio
{
    /// <summary>
    /// Кліпи звуку треку V7 (CC0, <c>Assets/ThirdParty/CC0/Audio</c>) з ключами з <c>audio_manifest.txt</c>.
    /// Заповнює редактор (<c>Editor/AudioLibraryBuilder</c>); ЯКИЙ кліп на подію — <c>SoundCueTable</c>.
    /// </summary>
    public sealed class AudioLibrary : MonoBehaviour
    {
        public string[] Keys = new string[0];
        public AudioClip[] Clips = new AudioClip[0];

        private Dictionary<string, AudioClip> _map;

        public AudioClip Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (_map == null)
            {
                _map = new Dictionary<string, AudioClip>();
                for (int i = 0; i < Keys.Length && i < Clips.Length; i++)
                    if (Clips[i] != null) _map[Keys[i]] = Clips[i];
            }
            AudioClip c;
            return _map.TryGetValue(key, out c) ? c : null;
        }
    }
}
