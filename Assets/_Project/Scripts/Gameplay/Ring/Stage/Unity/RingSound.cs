using System.Collections;
using Game.Core.Ring.Match;
using UnityEngine;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Звук рингу (ітерація 1): музика титулу й виходів, гул натовпу за ажіотажем, дзвін на початку й у кінці матчу, удари,
    /// падіння на мат, долоня рефері на відліку, оплески на великий момент. Усе — CC0 (Assets/ThirdParty/CC0/Audio/Ring,
    /// LICENSES.md). Власник 04.11.2026: «Звуку рингу немає зовсім … Я дозволяю знайти у інтернеті та завантажити».
    /// Звук двовимірний: камера кадрує ринг, позиційність без сенсу на ітерації 1. Не лінтується (тека Stage/Unity).
    /// </summary>
    public sealed class RingSound : MonoBehaviour
    {
        private RingStageAssets _a;
        private AudioSource _music, _crowd, _sfx, _sfx2;
        private float _musicTarget, _crowdTarget;
        private int _rr;

        public void Init(RingStageAssets assets)
        {
            _a = assets;
            _music = Src("Музика", true, 0f);
            _crowd = Src("Натовп", true, 0f);
            _sfx = Src("Ефекти", false, 0.9f);
            _sfx2 = Src("Ефекти 2", false, 0.8f);
            if (_a.sndCrowd != null) { _crowd.clip = _a.sndCrowd; _crowd.Play(); }
        }

        private AudioSource Src(string name, bool loop, float vol)
        {
            var go = new GameObject("Звук: " + name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.loop = loop;
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            s.volume = vol;
            return s;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_music != null) _music.volume = Mathf.MoveTowards(_music.volume, _musicTarget, dt * 0.6f);
            if (_crowd != null) _crowd.volume = Mathf.MoveTowards(_crowd.volume, _crowdTarget, dt * 0.5f);
            if (_music != null && _music.isPlaying && _music.volume <= 0.001f && _musicTarget <= 0f) _music.Stop();
        }

        // ============================ музика ============================

        public void TitleMusic() { Music(_a.musicTitle, 0.55f); }

        /// <summary>Музика виходу: Молот — своя, герой — своя, борці федерації — спільна.</summary>
        public void EntranceMusic(bool player, bool hammer)
        {
            Music(player ? _a.musicHero : hammer ? _a.musicHammer : _a.musicRoster, 0.7f);
            OneShot(_a.sndGong, 0.8f);
        }

        public void MusicOff() { _musicTarget = 0f; }

        private void Music(AudioClip c, float vol)
        {
            if (c == null) return;
            if (_music.clip != c || !_music.isPlaying)
            {
                _music.clip = c;
                _music.volume = 0f;
                _music.Play();
            }
            _musicTarget = vol;
        }

        // ============================ натовп ============================

        /// <summary>Гул залу: 0 — тиша (закулісся), далі за настроєм ажіотажу 0…3.</summary>
        public void Crowd(int mood) { _crowdTarget = mood < 0 ? 0f : 0.12f + 0.12f * Mathf.Clamp(mood, 0, 3); }

        public void Pop() { OneShot(_a.sndApplause, 0.75f, true); }

        // ============================ матч ============================

        public void BellStart() { OneShot(_a.sndBell, 0.9f); }

        public void BellEnd() { StartCoroutine(Repeat(_a.sndBellEnd, 3, 0.32f, 0.9f)); }

        /// <summary>Долоня рефері об мат — на мітці відліку його кліпа (count_1/2/3).</summary>
        public void Slap() { OneShot(_a.sndMatSlap, 0.9f); }

        /// <summary>Звук події матчу: за дією — удар, падіння, приземлення; відлік рефері — долоня об мат.</summary>
        public void OnEvent(MatchEvent ev, float delay, bool pinEnd)
        {
            AudioClip c = null;
            switch (ev.Kind)
            {
                case MatchEventKind.Kickout: StartCoroutine(Repeat(_a.sndMatSlap, 2, 0.45f, 0.9f, delay)); return;
                case MatchEventKind.MatchEnd:
                    // Утримання — три удари долонею; кінець відліком чи дискваліфікацією — лише дзвін (BellEnd).
                    if (pinEnd) StartCoroutine(Repeat(_a.sndMatSlap, 3, 0.45f, 0.9f, delay));
                    return;
                case MatchEventKind.Reverse: c = Pick(_a.sndSlam); break;
            }
            if (c == null)
                switch (ev.Action)
                {
                    case MatchActionId.Strike:
                    case MatchActionId.Dirty:
                    case MatchActionId.Desperate: c = Pick(_a.sndHit); break;
                    case MatchActionId.Throw:
                    case MatchActionId.Finisher:
                    case MatchActionId.IntoRopes:
                    case MatchActionId.IntoCorner:
                    case MatchActionId.HopeSpot: c = Pick(_a.sndSlam); break;
                    case MatchActionId.TopRope: c = _a.sndLand; break;
                }
            if (c != null) StartCoroutine(Delayed(c, delay, 0.85f));
        }

        private AudioClip Pick(AudioClip[] set)
        {
            if (set == null || set.Length == 0) return null;
            return set[_rr++ % set.Length];
        }

        private void OneShot(AudioClip c, float vol, bool second = false)
        {
            if (c == null) return;
            (second ? _sfx2 : _sfx).PlayOneShot(c, vol);
        }

        private IEnumerator Delayed(AudioClip c, float delay, float vol)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            OneShot(c, vol);
        }

        private IEnumerator Repeat(AudioClip c, int n, float gap, float vol, float delay = 0f)
        {
            if (c == null) yield break;
            if (delay > 0f) yield return new WaitForSeconds(delay);
            for (int i = 0; i < n; i++)
            {
                OneShot(c, vol);
                yield return new WaitForSeconds(gap);
            }
        }
    }
}
