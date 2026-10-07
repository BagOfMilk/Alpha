using Game.Core.Session;
using Game.Gameplay.UI;
using UnityEngine;

namespace Game.Gameplay.Audio
{
    /// <summary>
    /// Режисер звуку (Поправка №18.4, віха M1.19): музика й атмосфера за станом гри з плавним переходом,
    /// такти на нові події публічного журналу (<c>GameSession.DayLog</c>) і на кнопки інтерфейсу
    /// (<see cref="SoundSettings"/>), тихе вогнище під атмосферою села. ЩО грати — <see cref="SoundCueTable"/>;
    /// тут — лише джерела звуку. Чисел Напруги не читає (інваріант 3): лише стан і публічні події.
    /// </summary>
    public sealed class AudioDirector : MonoBehaviour
    {
        private const float FadeSeconds = 2.5f;
        private const string PrefsPrefix = "alpha.volume.";

        private AudioLibrary _library;
        private GameShell _shell;
        private AudioSource[] _music, _ambience;
        private AudioSource _fire, _sfx, _ui;
        private int _musicActive, _ambienceActive;
        private string _musicKey = "", _ambienceKey = "";
        private GameSession _seenSession;
        private int _seenLogCount;
        private int _counter;

        private void Awake()
        {
            _library = GetComponent<AudioLibrary>();
            _music = new[] { Source("music A", true), Source("music B", true) };
            _ambience = new[] { Source("ambience A", true), Source("ambience B", true) };
            _fire = Source("fire", true);
            _sfx = Source("sfx", false);
            _ui = Source("ui", false);
            foreach (SoundBus b in System.Enum.GetValues(typeof(SoundBus)))
                if (PlayerPrefs.HasKey(PrefsPrefix + b)) SoundSettings.Set(b, PlayerPrefs.GetFloat(PrefsPrefix + b));
            SoundSettings.Dirty = false;
        }

        private AudioSource Source(string sourceName, bool loop)
        {
            var go = new GameObject(sourceName);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = loop;
            s.spatialBlend = 0f;
            s.volume = 0f;
            return s;
        }

        private void Update()
        {
            if (_library == null) return;
            if (_shell == null) _shell = FindAnyObjectByType<GameShell>();
            var session = _shell != null ? _shell.Session : null;

            var state = session != null ? session.State : SessionState.Title;
            bool battle = state == SessionState.Battle;
            bool title = state == SessionState.Title || state == SessionState.Creation;
            bool night = state == SessionState.Night;
            bool evening = night || state == SessionState.Evening;

            Crossfade(_music, ref _musicActive, ref _musicKey,
                SoundCueTable.FileOf(SoundCueTable.MusicFor(battle, evening, title)), SoundBus.Music);
            Crossfade(_ambience, ref _ambienceActive, ref _ambienceKey,
                battle ? null : SoundCueTable.FileOf(SoundCueTable.AmbienceFor(battle, night, title)), SoundBus.Ambience);
            Bed(_fire, title || battle ? null : SoundCueTable.FireLoop, SoundSettings.Effective(SoundBus.Ambience) * 0.25f);

            PlayNewEvents(session);
            SoundCue cue;
            while (SoundSettings.TryDequeue(out cue)) Play(cue);

            if (SoundSettings.Dirty)
            {
                foreach (SoundBus b in System.Enum.GetValues(typeof(SoundBus)))
                    PlayerPrefs.SetFloat(PrefsPrefix + b, SoundSettings.Get(b));
                PlayerPrefs.Save();
                SoundSettings.Dirty = false;
            }
        }

        private void PlayNewEvents(GameSession session)
        {
            if (session == null) return;
            var log = session.DayLog;
            if (session != _seenSession || log.Count < _seenLogCount)
            {
                // Нова сесія, «Продовжити» чи нова доба (журнал скинуто) — старі події не озвучуємо.
                _seenSession = session;
                _seenLogCount = log.Count;
                return;
            }
            int played = 0;
            for (int i = _seenLogCount; i < log.Count && played < 3; i++)
            {
                var cue = SoundCueTable.ForEvent(log[i].Key);
                if (cue == SoundCue.None) continue;
                Play(cue);
                played++;
            }
            _seenLogCount = log.Count;
        }

        public void Play(SoundCue cue)
        {
            var clip = _library.Get(SoundCueTable.Pick(cue, _counter++));
            if (clip == null) return;
            var bus = SoundCueTable.BusOf(cue);
            (bus == SoundBus.Ui ? _ui : _sfx).PlayOneShot(clip, SoundSettings.Effective(bus));
        }

        private void Crossfade(AudioSource[] pair, ref int active, ref string current, string key, SoundBus bus)
        {
            if (key != current)
            {
                current = key;
                active = 1 - active;
                var clip = _library.Get(key);
                pair[active].clip = clip;
                if (clip != null) pair[active].Play(); else pair[active].Stop();
            }
            float target = SoundSettings.Effective(bus);
            float step = Time.unscaledDeltaTime / FadeSeconds;
            pair[active].volume = Mathf.MoveTowards(pair[active].volume, pair[active].clip != null ? target : 0f, step);
            var other = pair[1 - active];
            other.volume = Mathf.MoveTowards(other.volume, 0f, step);
            if (other.isPlaying && other.volume <= 0f) other.Stop();
        }

        private void Bed(AudioSource source, string key, float volume)
        {
            var clip = key != null ? _library.Get(key) : null;
            if (source.clip != clip)
            {
                source.clip = clip;
                if (clip != null) source.Play(); else source.Stop();
            }
            source.volume = Mathf.MoveTowards(source.volume, clip != null ? volume : 0f, Time.unscaledDeltaTime / FadeSeconds);
        }
    }
}
