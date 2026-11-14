using System.Collections.Generic;
using Game.Core.Ring;
using Game.Core.Ring.Match;
using Game.Gameplay.Ring.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.Ring.UI
{
    /// <summary>
    /// Виноска матчу: велике слово по центру ринга на мить (огляд фану 06.11.2026, R1/F2: кидок закінчувався рядком журналу).
    /// Чистий успіх — найбільше й золоте з «ударом» масштабу, «частково» — менше й біле, провал — червоне й тихе; «на два»,
    /// фінішер і кінець матчу — найгучніші. Черга: «РАЗ» → «ДВА» → «ВИРВАВСЯ!» ідуть одна за одною в такт відліку рефері.
    /// Шар не ловить мишу.
    /// </summary>
    public sealed class RingCallout
    {
        public readonly VisualElement Layer;
        private readonly VisualElement _plate;
        private readonly Label _word;
        private readonly Label _sub;

        private struct Item { public string Word, Sub; public Color Color; public float Size, Dur; }

        private readonly Queue<Item> _q = new Queue<Item>();
        private Item _cur;
        private float _t = -1f;

        public RingCallout()
        {
            Layer = RingUi.Column();
            Layer.pickingMode = PickingMode.Ignore;
            Layer.style.position = Position.Absolute;
            Layer.style.left = 0f; Layer.style.right = 0f; Layer.style.top = Length.Percent(12.5f);
            Layer.style.alignItems = Align.Center;
            // Темна підкладка під словом І підрядком: червоне «Провал» губилось на сорочці рефері й натовпі, а підрядок
            // «Ти / Суперник» під плитою — на смугах рефері й світлому маті (огляд слоту 31: h4x3 275, h1080 435).
            _plate = RingUi.Column();
            _plate.pickingMode = PickingMode.Ignore;
            _plate.style.alignItems = Align.Center;
            _plate.style.backgroundColor = RingTheme.WithAlpha(RingTheme.Bg, BrandTokens.AlphaUnderlay);
            _plate.style.paddingLeft = 18f; _plate.style.paddingRight = 18f; _plate.style.paddingTop = 2f; _plate.style.paddingBottom = 6f;
            _plate.style.borderTopLeftRadius = 8f; _plate.style.borderTopRightRadius = 8f;
            _plate.style.borderBottomLeftRadius = 8f; _plate.style.borderBottomRightRadius = 8f;
            _word = RingUi.Lbl("", 40f, RingUi.Gold, true);
            _word.pickingMode = PickingMode.Ignore;
            _word.style.unityTextAlign = TextAnchor.MiddleCenter;
            _sub = RingUi.Lbl("", 16f, RingUi.Text);
            _sub.pickingMode = PickingMode.Ignore;
            _sub.style.unityTextAlign = TextAnchor.MiddleCenter;
            _sub.style.marginTop = -2f;
            _plate.Add(_word);
            _plate.Add(_sub);
            Layer.Add(_plate);
            Layer.style.opacity = 0f;
        }

        public bool Showing { get { return _t >= 0f || _q.Count > 0; } }

        public void Push(string word, string sub, Color color, float size, float dur)
        {
            _q.Enqueue(new Item { Word = word, Sub = sub ?? "", Color = color, Size = size, Dur = dur });
        }

        /// <summary>Показати одразу, перебивши поточне слово й чергу (розв'язка відліку — точно на кікауті).</summary>
        public void Now(string word, string sub, Color color, float size, float dur)
        {
            _q.Clear();
            _t = -1f;
            Push(word, sub, color, size, dur);
        }

        public void Clear() { _q.Clear(); _t = -1f; Layer.style.opacity = 0f; }

        public void Tick(float dt)
        {
            if (_t < 0f)
            {
                if (_q.Count == 0) return;
                _cur = _q.Dequeue();
                _t = 0f;
                // Порожнє слово — пауза в черзі: плиту ховаємо цілком, порожній підрядок не тримає під словом смужку.
                _plate.style.display = string.IsNullOrEmpty(_cur.Word) ? DisplayStyle.None : DisplayStyle.Flex;
                _word.text = _cur.Word;
                _word.style.color = _cur.Color;
                _sub.text = _cur.Sub;
                _sub.style.display = string.IsNullOrEmpty(_cur.Sub) ? DisplayStyle.None : DisplayStyle.Flex;
            }
            _t += dt;
            // Удар масштабу на вході (0,12 с: 1,35 → 1), утримання, згасання за останні 0,25 с.
            float pop = Mathf.Clamp01(_t / 0.12f);
            _word.style.fontSize = _cur.Size * Mathf.Lerp(1.35f, 1f, pop);
            float fade = _cur.Dur - _t;
            Layer.style.opacity = Mathf.Clamp01(fade / 0.25f);
            if (_t >= _cur.Dur) { _t = -1f; Layer.style.opacity = 0f; }
        }
    }

    /// <summary>Що сказати на такт режисера: подія → слово, підрядок і вага (розмір, колір, тривалість).</summary>
    public static class RingCallouts
    {
        /// <param name="phase">Фаза такту: 0 — контакт; утримання — 1 «раз», 2 «два», 3 — розв'язка (<c>IRingStage.BeatPhase</c>).</param>
        /// <param name="rescued">За утриманням іде порятунок напарником (<see cref="MatchScreenModel.RescueFollows"/>): «три» не було.</param>
        public static void Push(RingCallout c, MatchEvent ev, int phase, RingText t, MatchWinner? winner, bool rescued = false)
        {
            switch (ev.Kind)
            {
                case MatchEventKind.Kickout:
                    // Відлік рефері: «раз», «два» — і вирвався на два (найгучніший момент вечора, gdd §4.6) чи «три». Кожне
                    // слово — на долоні рефері (мітки count_1/count_2 його кліпа), розв'язка — на мітці kickout чи count_3
                    // (огляд 08.11.2026: слова йшли за таймером і обганяли відлік).
                    if (phase == 1) c.Now(t.Get("match.call.one"), null, RingUi.Text, 34f, 0.7f);
                    else if (phase == 2) c.Now(t.Get("match.call.two"), null, RingUi.Warn, 40f, 0.7f);
                    // Напарник розірвав рахунок: рефері зупинився на «два з половиною» — слова «три» немає (огляд слоту 33).
                    else if (ev.Tier == RollTier.Miss) { if (!rescued) c.Now(t.Get("match.call.three"), null, RingUi.Bad, 64f, 1.4f); }
                    // «ВИРВАЛАСЯ!» — у роді того, хто вирвався (Actor події Kickout; P2-6).
                    else c.Now(ev.Tier == RollTier.Partial ? t.Get("match.call.kickout.two") : RingLabels.ForSide(t, "match.call.kickout", ev.Actor),
                        ev.Perk != null ? Who(t, ev) + " · " + RingLabels.PerkName(t, ev.Perk) : Who(t, ev), RingUi.Gold, 54f, 1.5f);
                    return;
                case MatchEventKind.MatchEnd:
                    if (winner == null) return;
                    string key = winner == MatchWinner.Player ? "match.call.win" : winner == MatchWinner.Rival ? "match.call.loss" : "match.call.draw";
                    Color col = winner == MatchWinner.Player ? RingUi.Gold : winner == MatchWinner.Rival ? RingTheme.Defeat : RingUi.Text;
                    c.Push(t.Get(key), null, col, 72f, MatchScreenView.ResultMoment);
                    return;
                case MatchEventKind.Reverse:
                    if (ev.Tier == RollTier.Strong) c.Push(t.Get("match.call.reverse"), Who(t, ev), RingUi.Gold, 46f, 1.1f);
                    return;
                case MatchEventKind.Action:
                    if (!ev.HasRoll) return;
                    string sub = Who(t, ev) + " · " + RingLabels.ActionName(t, ev.Action) + " · " + ev.Total;
                    if (ev.Perk != null) sub += " · " + RingLabels.PerkName(t, ev.Perk);   // перк з дією змінив кидок: спершу хто (FX-02), перк — пояснення
                    if (ev.Action == MatchActionId.Finisher && ev.Tier != RollTier.Miss)
                    {
                        c.Push(t.Get("match.call.finisher"), sub, RingUi.Gold, 62f, 1.6f);
                        return;
                    }
                    switch (ev.Tier)
                    {
                        case RollTier.Strong: c.Push(t.Get("match.call.strong"), sub, RingUi.Gold, 46f, 1.1f); break;
                        case RollTier.Partial: c.Push(t.Get("match.call.partial"), sub, RingUi.Text, 26f, 0.8f); break;
                        default: c.Push(t.Get("match.call.miss"), sub, RingUi.Bad, 26f, 0.8f); break;
                    }
                    return;
            }
        }

        private static string Who(RingText t, MatchEvent ev)
        {
            return ev.Actor == MatchSide.Player ? t.Get("match.call.you") : RingLabels.ForSide(t, "match.call.rival", MatchSide.Rival);
        }
    }
}
