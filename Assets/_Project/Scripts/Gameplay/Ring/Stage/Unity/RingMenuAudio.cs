using Game.Gameplay.Ring.Models;
using Game.Gameplay.Ring.UI.Menus;
using UnityEngine;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Програвач меню (ТЗ B08 §5): музика з кросфейдом 0,8 с між двома джерелами (музика не обривається при зміні екрана),
    /// звуки меню, тестовий звук шини в налаштуваннях. Гучність — шини <see cref="RingAudioMix"/> (Master × шина); вікно
    /// без фокусу — тиша, якщо так обрано. Не лінтується (тека Stage/Unity).
    /// </summary>
    public sealed class RingMenuAudio : MonoBehaviour, IRingMenuAudio
    {
        private RingMenuAssets _a;
        private AudioSource _musA, _musB, _sfx, _voice;
        private AudioSource _cur;
        private float _curLevel, _oldLevel;
        private AudioSource _old;
        private MenuMusic _track = MenuMusic.None;
        private const float MusicLevel = 1f;   // кліпи вже зведені на −16 LUFS; рівень — лише шиною

        private void Awake() { Init(GetComponent<RingMenuAssets>()); }

        public void Init(RingMenuAssets assets)
        {
            _a = assets;
            _musA = Src("Музика меню A", true);
            _musB = Src("Музика меню B", true);
            _sfx = Src("Звуки меню", false);
            _voice = Src("Голос (тест)", false);
            // одноразові звуки: гучність джерела 1, рівень — шиною через volumeScale у PlayOneShot
            _sfx.volume = 1f;
            _voice.volume = 1f;
            RingMenuServices.Audio = this;
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(RingMenuServices.Audio, this)) RingMenuServices.Audio = null;
        }

        private void OnApplicationFocus(bool focus) { RingAudioMix.Current.Focused = focus; }

        private AudioSource Src(string name, bool loop)
        {
            var go = new GameObject("Звук: " + name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.loop = loop;
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            s.volume = 0f;
            s.ignoreListenerPause = true;
            return s;
        }

        private AudioClip Clip(MenuMusic m)
        {
            if (_a == null) return null;
            switch (m)
            {
                case MenuMusic.Login: return _a.musicLogin;
                case MenuMusic.Loading: return _a.musicLoading;
                case MenuMusic.Settings: return _a.musicSettings;
                default: return null;
            }
        }

        public void PlayMusic(MenuMusic track)
        {
            if (track == _track) return;
            _track = track;
            var clip = Clip(track);
            // поточне джерело стає «старим» і згасає; нове (інше з пари) наростає — кросфейд без обриву
            if (_cur != null && _cur.isPlaying)
            {
                if (_old != null && _old != _cur) _old.Stop();   // третій трек поспіль: попередній «старий» не лишається висіти
                _old = _cur; _oldLevel = _curLevel;
            }
            // ворота звуку (рішення власника 08.11.2026): грає лише схвалене в бібліотеці звуку
            if (!RingSoundGate.Allows(clip)) { _cur = null; _curLevel = 0f; return; }
            var next = _cur == _musA ? _musB : _musA;
            if (next == _old) { _old = _cur; _oldLevel = _curLevel; }
            next.clip = clip;
            next.time = 0f;
            next.volume = 0f;
            next.Play();
            _cur = next;
            _curLevel = 0f;
        }

        public void Sfx(MenuSfx cue)
        {
            if (_a == null || _a.sfx == null) return;
            int i = (int)cue;
            if (i < 0 || i >= _a.sfx.Length || !RingSoundGate.Allows(_a.sfx[i])) return;
            _sfx.PlayOneShot(_a.sfx[i], RingAudioMix.Current.Gain(RingBus.Sfx));
        }

        public void TestBus(RingBus bus)
        {
            if (_a == null || _a.sfx == null) return;
            switch (bus)
            {
                case RingBus.Master: Play(_sfx, MenuSfx.Confirm, RingAudioMix.Current.Gain(RingBus.Master)); break;   // PlayOneShot
                case RingBus.Sfx: break;      // крок повзунка вже звучить на шині ефектів
                case RingBus.Voice: Play(_voice, MenuSfx.LoginOk, RingAudioMix.Current.Gain(RingBus.Voice)); break;
                default: break;               // музика грає — її й так чути
            }
        }

        private void Play(AudioSource s, MenuSfx cue, float gain)
        {
            var c = _a.sfx[(int)cue];
            if (!RingSoundGate.Allows(c)) return;
            s.Stop();
            s.PlayOneShot(c, gain);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float rate = dt / Mathf.Max(0.05f, RingAudioMix.CrossfadeSeconds);
            float g = RingAudioMix.Current.Gain(RingBus.Music);
            if (_cur != null)
            {
                _curLevel = Mathf.Min(1f, _curLevel + rate);
                _cur.volume = _curLevel * MusicLevel * g;
            }
            if (_old != null)
            {
                _oldLevel = Mathf.Max(0f, _oldLevel - rate);
                _old.volume = _oldLevel * MusicLevel * g;
                if (_oldLevel <= 0f) { _old.Stop(); _old = null; }
            }
        }
    }
}
