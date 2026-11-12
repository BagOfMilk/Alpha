using System;
using System.Collections.Generic;
using Game.Core.Ring;
using Game.Core.Ring.Match;

namespace Game.Gameplay.Ring.Models
{
    /// <summary>Що сталося у вікні QTE за цей Tick (прапорці: за один Tick може бути й відкриття, й пропуск).</summary>
    [Flags]
    public enum QteTick { None = 0, Opened = 1, Missed = 2 }

    /// <summary>
    /// Автомат вікна QTE (рішення власника 08.11.2026: «лише видовище»). Час — аргументи (секунди ігрового часу), ніякого Unity:
    /// режисер відкриває вікно на мітках кліпа <c>qte_open</c>/<c>qte_close</c> і гоне <see cref="Tick"/> щокадру. Натиск у вікні —
    /// <see cref="Press"/> повертає номер події для <see cref="MatchEngine.RegisterFlourish"/> РІВНО раз; без натиску до закриття —
    /// «пропуск» рівно раз; <see cref="Cancel"/> гасить вікно без пропуску. Вимкнено в налаштуваннях (<see cref="RingMatchPrefs.Qte"/>,
    /// читається в мить відкриття) — вікно не відкривається. Кидків, ступенів, ушкоджень і ініціативи автомат не торкається.
    /// </summary>
    public sealed class MatchQteWindow
    {
        private enum Phase { Idle, Armed, Open, Done }

        private Phase _phase = Phase.Idle;
        private int _seq = -1;
        private float _tOpen, _tClose;
        private QteTick _pending;

        /// <summary>Вікно чекає відкриття або відкрите.</summary>
        public bool IsActive { get { return _phase == Phase.Armed || _phase == Phase.Open; } }

        /// <summary>Вікно відкрите (час усередині [відкриття, закриття]).</summary>
        public bool IsOpen { get { return _phase == Phase.Open; } }

        /// <summary>Номер події, якій дістанеться видовище.</summary>
        public int Seq { get { return _seq; } }

        /// <summary>Скільки вікна лишилось (1 — щойно відкрилось, 0 — закрите чи не відкрите) — для індикатора.</summary>
        public float Remaining01(float t)
        {
            if (_phase != Phase.Open || _tClose <= _tOpen) return 0f;
            return Math.Max(0f, Math.Min(1f, (_tClose - t) / (_tClose - _tOpen)));
        }

        /// <summary>
        /// Завести вікно на [tOpen, tClose]. false — не відкрито: QTE вимкнено, немає події (<paramref name="seq"/> &lt; 0) чи вікно
        /// порожнє. Нове вікно замінює старе (без пропуску для старого).
        /// </summary>
        public bool Open(int seq, float tOpen, float tClose)
        {
            Cancel();
            if (!RingMatchPrefs.Qte || seq < 0 || !(tClose > tOpen)) return false;
            _phase = Phase.Armed;
            _seq = seq; _tOpen = tOpen; _tClose = tClose;
            _pending = QteTick.None;
            return true;
        }

        /// <summary>Рух часу: відкриття (<see cref="QteTick.Opened"/>) і пропуск (<see cref="QteTick.Missed"/>) — кожне раз.</summary>
        public QteTick Tick(float t)
        {
            Advance(t);
            var r = _pending;
            _pending = QteTick.None;
            return r;
        }

        /// <summary>
        /// Натиск у момент <paramref name="t"/>. true — потрапив у вікно, <paramref name="seq"/> — для RegisterFlourish (вікно
        /// закрите, друге натискання вже нікуди не потрапить); false — до відкриття (ігнор, вікно живе), після закриття (це пропуск)
        /// чи вікна нема.
        /// </summary>
        public bool Press(float t, out int seq)
        {
            seq = -1;
            Advance(t);
            if (_phase != Phase.Open) return false;
            seq = _seq;
            _phase = Phase.Done;
            return true;
        }

        /// <summary>Погасити вікно без пропуску (кінець матчу, пауза, скіп).</summary>
        public void Cancel()
        {
            _phase = Phase.Idle;
            _seq = -1;
            _pending = QteTick.None;
        }

        private void Advance(float t)
        {
            if (_phase == Phase.Armed && t >= _tOpen)
            {
                _phase = Phase.Open;
                _pending |= QteTick.Opened;
            }
            if (_phase == Phase.Open && t > _tClose)
            {
                _phase = Phase.Done;
                _pending |= QteTick.Missed;
            }
        }
    }

    /// <summary>
    /// Натиск QTE від хоста до режисера (Ф3 п. 4). Хост (лінтується) не бачить сцени Unity, тож режисер сам реєструє тут
    /// свій <c>QtePress</c>, поки живий. Немає режисера чи вікна — <see cref="TryPress"/> false, клавіша йде далі як завжди.
    /// </summary>
    public static class MatchQteInput
    {
        private static Func<bool> _press;

        public static void Bind(Func<bool> press) { _press = press; }

        /// <summary>Знімає лише свій зв'язок: новий режисер, що вже зареєструвався, не зникає.</summary>
        public static void Unbind(Func<bool> press) { if (_press == press) _press = null; }

        public static bool TryPress() { var p = _press; return p != null && p(); }
    }

    /// <summary>
    /// Звідки брати номер події для видовища: ядро (<see cref="MatchEngine.RegisterFlourish"/>) приймає лише влучний прийом
    /// ГРАВЦЯ, тож вікно кліпа треба прив'язати до такої події. Чиста функція — без Unity.
    /// </summary>
    public static class MatchQteSource
    {
        /// <summary>
        /// Стрибок із каната гравця, не провал — його ж подія. «Терпіти» (подія захисника) на кліпі <c>*_Crank</c>, де захват тримає
        /// гравець (захисник — суперник) — остання влучна дія «Больовий» гравця. Захват суперника, провал і все інше — −1 (вікно не
        /// відкривати).
        /// </summary>
        public static int SeqFor(IReadOnlyList<MatchEvent> log, MatchEvent ev, string clip)
        {
            if (ev == null) return -1;
            if (ev.Kind == MatchEventKind.Action)
                return ev.Action == MatchActionId.TopRope && ev.Actor == MatchSide.Player && ev.HasRoll && ev.Tier != RollTier.Miss ? ev.Seq : -1;
            if (ev.Kind != MatchEventKind.Endure || ev.Actor != MatchSide.Rival || log == null) return -1;
            if (clip == null || clip.IndexOf("_Crank", StringComparison.Ordinal) < 0) return -1;
            for (int i = Math.Min(ev.Seq, log.Count) - 1; i >= 0; i--)
            {
                var s = log[i];
                if (s.Kind == MatchEventKind.Action && s.Action == MatchActionId.Submission && s.Actor == MatchSide.Player)
                    return s.HasRoll && s.Tier != RollTier.Miss ? s.Seq : -1;
            }
            return -1;
        }
    }
}
