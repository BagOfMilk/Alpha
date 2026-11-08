using System.Collections;
using System.Collections.Generic;
using Game.Core.Ring.Match;
using Game.Gameplay.Ring.Models;
using UnityEngine;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Звук рингу (ітерація 1): музика титулу й виходів, гул натовпу за ажіотажем, дзвін на початку й у кінці матчу, удари,
    /// падіння на мат, долоня рефері на відліку, оплески на великий момент, QTE. ЩО звучить на подію — таблиця
    /// <see cref="RingSoundMap"/> (docs/ring/audio/EVENT_SOUND.md); ЧИ звучить — лише схвалене власником (<see cref="RingSoundGate"/>).
    /// Кліпи: набір сцени (CC0, Assets/ThirdParty/CC0/Audio/Ring, LICENSES.md) і нові звуки подій з Resources/RingSound
    /// (Assets/Art/Ring/Sound/Resources/RingSound, власна генерація tools/ring_audio). Звук двовимірний і моно: камера кадрує ринг,
    /// позиційність без сенсу на ітерації 1 (імпорт — Editor/RingSoundImport). Не лінтується (тека Stage/Unity).
    /// </summary>
    public sealed class RingSound : MonoBehaviour
    {
        private RingStageAssets _a;
        private AudioSource _music, _crowd, _sfx, _sfx2;
        private float _musicTarget, _crowdTarget;
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private readonly Dictionary<RingCue, int> _rr = new Dictionary<RingCue, int>();
        private readonly List<AudioClip> _pick = new List<AudioClip>();

        public void Init(RingStageAssets assets)
        {
            _a = assets;
            _music = Src("Музика", true, 0f);
            _crowd = Src("Натовп", true, 0f);
            _sfx = Src("Ефекти", false, 0.9f);
            _sfx2 = Src("Ефекти 2", false, 0.8f);
            Register(_a.sndCrowd, _a.sndApplause, _a.sndBell, _a.sndBellEnd, _a.sndGong, _a.sndMatSlap, _a.sndLand);
            Register(_a.sndHit);
            Register(_a.sndSlam);
            // ворота звуку (рішення власника 08.11.2026): грає лише схвалене в бібліотеці звуку
            var bed = Pick(RingCue.CrowdBed);
            if (bed != null && RingSoundGate.Allows(bed)) { _crowd.clip = bed; _crowd.Play(); }
            // Меню (ТЗ B08): хост вмикає й вимикає музику зали, коли гравець входить у гру чи повертається на титул.
            RingMenuServices.GameMusic = on => { if (on) Music(_a.musicTitle, 0.55f); else MusicOff(); };
        }

        private void OnDestroy() { RingMenuServices.GameMusic = null; }

        // Гучність за шинами налаштувань (ТЗ B08 §5): музика — шина «Музика», натовп і удари — «Ефекти»; Master — в обох.
        private static float MusicGain { get { return RingAudioMix.Current.Gain(RingBus.Music); } }
        private static float SfxGain { get { return RingAudioMix.Current.Gain(RingBus.Sfx); } }

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
            if (_music != null) _music.volume = Mathf.MoveTowards(_music.volume, _musicTarget * MusicGain, dt * 0.6f);
            if (_crowd != null) _crowd.volume = Mathf.MoveTowards(_crowd.volume, _crowdTarget * SfxGain, dt * 0.5f);
            if (_music != null && _music.isPlaying && _music.volume <= 0.001f && _musicTarget <= 0f) _music.Stop();
        }

        // ============================ кліпи ============================

        private void Register(params AudioClip[] clips)
        {
            if (clips == null) return;
            for (int i = 0; i < clips.Length; i++)
                if (clips[i] != null) _clips[clips[i].name] = clips[i];
        }

        /// <summary>Кліп слота бібліотеки: з набору сцени або (нові звуки подій) з Resources/RingSound; відсутність теж кешується.</summary>
        private AudioClip Clip(string slot)
        {
            AudioClip c;
            if (_clips.TryGetValue(slot, out c)) return c;
            c = Resources.Load<AudioClip>(RingSoundMap.ResourcesFolder + "/" + slot);
            _clips[slot] = c;
            return c;
        }

        /// <summary>Один зі СХВАЛЕНИХ кліпів звуку, по колу; null — нічого не схвалено (подія мовчить).</summary>
        private AudioClip Pick(RingCue cue)
        {
            var slots = RingSoundMap.SlotsOf(cue);
            _pick.Clear();
            for (int i = 0; i < slots.Count; i++)
            {
                var c = Clip(slots[i]);
                if (c != null && RingSoundGate.Allows(c)) _pick.Add(c);
            }
            if (_pick.Count == 0) return null;
            int n;
            _rr.TryGetValue(cue, out n);
            _rr[cue] = n + 1;
            return _pick[n % _pick.Count];
        }

        /// <summary>Зіграти звук таблиці (із затримкою — на мітку контакту).</summary>
        public void Play(RingCue cue, float vol = 0.85f, float delay = 0f, bool second = false)
        {
            var c = Pick(cue);
            if (c == null) return;
            if (delay > 0f) StartCoroutine(Delayed(c, delay, vol, second));
            else OneShot(c, vol, second);
        }

        // ============================ музика ============================

        /// <summary>Музика вступу: поки меню веде музику (тема входу, ТЗ B08), зала мовчить; без програвача меню — стара петля.</summary>
        public void TitleMusic()
        {
            if (RingMenuServices.Audio != null && RingMenuServices.MenuMusicActive) { MusicOff(); return; }
            Music(_a.musicTitle, 0.55f);
        }

        /// <summary>
        /// Музика виходу: Молот — своя, герой — своя, борці федерації — спільна. Гонг — стінгер на ударі екрана виходу
        /// (ТЗ B07 п. 4: «удар = перший звук»; <paramref name="hitIn"/> — секунд до удару), там же оплески залу, якщо
        /// вихід не лиходійський (<paramref name="cheer"/>; лиходію — лише гул, ENTRANCE_PROFILE.md біт 5). Обидва — звуки таблиці
        /// (<see cref="RingCue.Entrance"/>, <see cref="RingCue.Pop"/>), тож проходять ворота звуку.
        /// </summary>
        public void EntranceMusic(bool player, bool hammer, double hitIn, bool cheer)
        {
            // До удару — лише гул залу й тизер екрана; тема вступає разом зі стінгером на ударі.
            MusicOff();
            if (_hitCo != null) StopCoroutine(_hitCo);
            _hitCo = StartCoroutine(OnHit(player ? _a.musicHero : hammer ? _a.musicHammer : _a.musicRoster, (float)hitIn, cheer));
        }

        private Coroutine _hitCo;

        /// <summary>Скасувати відкладений удар виходу (пропуск виходу): тема й гонг не звучать уже в матчі.</summary>
        public void CancelEntranceHit()
        {
            if (_hitCo != null) { StopCoroutine(_hitCo); _hitCo = null; }
        }

        private System.Collections.IEnumerator OnHit(AudioClip theme, float after, bool cheer)
        {
            if (after > 0f) yield return new WaitForSeconds(after);
            Music(theme, 0.7f);
            Play(RingCue.Entrance, 0.8f);                                    // удар виходу — слот gong_01 таблиці
            if (cheer) Play(RingCue.Pop, 0.6f, 0f, true);                    // оплески залу — слот applause_hall
            _hitCo = null;
        }

        public void MusicOff() { _musicTarget = 0f; }

        private void Music(AudioClip c, float vol)
        {
            if (!RingSoundGate.Allows(c)) return;
            if (_music.clip != c || !_music.isPlaying)
            {
                _music.clip = c;
                _music.volume = 0f;
                _music.Play();
            }
            _musicTarget = vol;
        }

        // ============================ натовп ============================

        /// <summary>Гул залу: −1 — тиша (закулісся), далі за настроєм 0…3 (матч) і до 4 (вихід популярного борця).</summary>
        public void Crowd(int mood) { _crowdTarget = mood < 0 ? 0f : 0.12f + 0.12f * Mathf.Clamp(mood, 0, 4); }

        public void Pop() { Play(RingCue.Pop, 0.75f, 0f, true); }

        // ============================ матч ============================

        public void BellStart() { Play(RingCue.BellStart, 0.9f); }

        public void BellEnd() { StartCoroutine(Repeat(RingCue.BellEnd, 3, 0.32f, 0.9f)); }

        /// <summary>Долоня рефері об мат — на мітці відліку його кліпа (count_1/2/3).</summary>
        public void Slap() { Play(RingCue.MatSlap, 0.9f); }

        /// <summary>
        /// QTE (переробка бою, рішення власника 08.11.2026 «Лише видовище»): поява й пропуск кличе режисер матчу; вдале QTE звучить
        /// подією Flourish через <see cref="OnEvent"/>.
        /// </summary>
        public void Qte(RingQte q) { Play(q == RingQte.Prompt ? RingCue.QtePrompt : RingCue.QteMiss, q == RingQte.Prompt ? 0.7f : 0.85f); }

        // Бій за рингом: склейка звучить, коли кадр уже на новому місці; відлік — після контакту, цифра за цифрою.
        private const float SurgeDelay = 0.35f, CountDelay = 0.7f, CountGap = 0.6f;

        /// <summary>
        /// Звук події матчу за таблицею <see cref="RingSoundMap"/> (зі ступенем кидка й місцем пари) і звуки її наслідків: склейка місця
        /// (Place) — зал здіймається, відлік за рингом (Count) — по звуку на цифру. Утримання й кікаут — долоня рефері, повтором.
        /// Кінець матчу відліком чи подвійним відліком: останні цифри звучать на дії, дзвін — режисер (BellEnd).
        /// </summary>
        public void OnEvent(MatchEvent ev, float delay, bool pinEnd)
        {
            switch (ev.Kind)
            {
                case MatchEventKind.Kickout: StartCoroutine(Repeat(RingCue.MatSlap, 2, 0.45f, 0.9f, delay)); return;
                case MatchEventKind.MatchEnd:
                    // Утримання — три удари долонею; кінець відліком чи дискваліфікацією — лише дзвін (BellEnd).
                    if (pinEnd) StartCoroutine(Repeat(RingCue.MatSlap, 3, 0.45f, 0.9f, delay));
                    return;
            }
            var cue = RingSoundMap.ForEvent(ev);
            // Гул залу вже звучить петлею (гучність веде ажіотаж) — окремого удару в цієї події немає.
            if (cue != RingCue.None && cue != RingCue.CrowdBed)
            {
                bool pop = cue == RingCue.Pop;
                Play(cue, pop ? 0.6f : 0.85f, delay, pop);
            }
            bool surged = false, counted = false;
            for (int i = 0; i < ev.Effects.Count; i++)
            {
                var fx = ev.Effects[i];
                var fc = RingSoundMap.ForEffect(fx.Kind);
                if (fc == RingCue.CrowdSurge && !surged) { surged = true; Play(RingCue.CrowdSurge, 0.7f, delay + SurgeDelay, true); }
                else if (fc == RingCue.CountOut && !counted && fx.Amount > 0)
                {
                    counted = true;
                    StartCoroutine(Repeat(RingCue.CountOut, Mathf.Min(fx.Amount, RingSoundMap.MaxCountTicks), CountGap, 0.8f, delay + CountDelay));
                }
            }
        }

        private void OneShot(AudioClip c, float vol, bool second = false)
        {
            if (!RingSoundGate.Allows(c)) return;
            (second ? _sfx2 : _sfx).PlayOneShot(c, vol * SfxGain);
        }

        private IEnumerator Delayed(AudioClip c, float delay, float vol, bool second)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            OneShot(c, vol, second);
        }

        private IEnumerator Repeat(RingCue cue, int n, float gap, float vol, float delay = 0f)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            for (int i = 0; i < n; i++)
            {
                Play(cue, vol);
                yield return new WaitForSeconds(gap);
            }
        }
    }
}
