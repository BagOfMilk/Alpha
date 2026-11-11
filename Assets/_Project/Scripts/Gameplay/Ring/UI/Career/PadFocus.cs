using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Game.Gameplay.Ring.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.Ring.UI
{
    /// <summary>
    /// Ціль фокусу без миші на екранах кар'єри (P3 геймпад, 11.11.2026): плитка дії, золота плита, вкладка шухляди, кнопка,
    /// будь-який позначений елемент (<see cref="PadMark"/>).
    /// </summary>
    public interface IPadTarget
    {
        /// <summary>Натискається (недоступну теж можна обрати фокусом — щоб прочитати причину, — але не натиснути).</summary>
        bool PadEnabled { get; }
        /// <summary>Ключ, за яким фокус повертається на «ту саму» ціль після перебудови екрана (Refresh → Root.Clear()).</summary>
        string PadKey { get; }
        /// <summary>Куди стати першим: менший — раніше (плитка дії 1, головна дія 2, кнопка 3, решта 4, «відмовитись» 6).</summary>
        int PadRank { get; }
        /// <summary>Фокус прийшов / пішов: той самий вигляд і той самий опис, що при наведенні мишею.</summary>
        void PadHot(bool on);
        void PadActivate();
        /// <summary>← → на перемикачі (карусель «Тіло», ряд кольорів): true — значення змінилось, фокус не рухається.</summary>
        bool PadStep(int delta);
    }

    /// <summary>
    /// Позначка цілі для елемента, що сам не <see cref="IPadTarget"/> (кнопка <c>RingUi</c>, рядок, карусель, поле імені).
    /// Живе поруч з елементом (слабке посилання): помер елемент — померла позначка.
    /// </summary>
    public sealed class PadMark : IPadTarget
    {
        private static readonly ConditionalWeakTable<VisualElement, PadMark> Marks = new ConditionalWeakTable<VisualElement, PadMark>();

        private readonly VisualElement _el;
        private readonly Action _activate;
        private readonly Action<int> _step;
        private Action<bool> _hot;
        private VisualElement _frame;

        public bool Enabled;
        public string Key;
        public int Rank;

        private PadMark(VisualElement el, Action activate, Action<int> step) { _el = el; _activate = activate; _step = step; }

        /// <summary>
        /// Позначити елемент ціллю. <paramref name="activate"/> = null — недоступна. <paramref name="hot"/> = null — фокус показує рамка
        /// 2 px кольору тексту по краю елемента (мова обводки золотої плити; У0 11.11.2026). Повторний виклик доповнює позначку.
        /// </summary>
        public static PadMark Mark(VisualElement el, Action activate, string key, int rank = 4, Action<bool> hot = null, Action<int> step = null)
        {
            if (el == null) return null;
            PadMark m;
            if (Marks.TryGetValue(el, out m)) Marks.Remove(el);
            m = new PadMark(el, activate, step) { Enabled = activate != null, Key = key, Rank = rank };
            if (hot != null) m._hot = hot;
            Marks.Add(el, m);
            return m;
        }

        /// <summary>Ще одна реакція на фокус (рядок-підказка кнопки, опис рядка).</summary>
        public static void AddHot(VisualElement el, Action<bool> hot)
        {
            PadMark m;
            if (el == null || hot == null || !Marks.TryGetValue(el, out m)) return;
            m._hot = m._hot == null ? hot : m._hot + hot;
        }

        public static PadMark Of(VisualElement el)
        {
            PadMark m;
            return el != null && Marks.TryGetValue(el, out m) ? m : null;
        }

        public bool PadEnabled { get { return Enabled; } }
        public string PadKey { get { return Key; } }
        public int PadRank { get { return Rank; } }

        public void PadHot(bool on)
        {
            if (_hot != null) { _hot(on); return; }
            if (on && _frame == null)
            {
                _frame = PadFocus.Frame();
                _el.Add(_frame);
            }
            else if (!on && _frame != null)
            {
                _frame.RemoveFromHierarchy();
                _frame = null;
            }
        }

        public void PadActivate() { if (Enabled && _activate != null) _activate(); }

        public bool PadStep(int delta)
        {
            if (_step == null) return false;
            _step(delta);
            return true;
        }
    }

    /// <summary>
    /// Фокус без миші на одному екрані кар'єри (P3 геймпад, 11.11.2026). Цілі збираються з дерева екрана щоразу, коли треба:
    /// екран перебудовується після кожної дії, тож тримаємо не елемент, а ключ цілі (<see cref="PadNavMath.Restore"/>).
    /// Режим без миші один на гру (<see cref="Active"/>): хрестовина чи стрілки вмикають його, рух миші вимикає; поки він
    /// увімкнений, наведення мишею не перехоплює вигляд фокусу (як у меню: <c>RingMenuInput.MouseArmed</c>).
    /// </summary>
    public sealed class PadFocus
    {
        /// <summary>Режим без миші: фокус видно, Enter / A натискає ціль у фокусі.</summary>
        public static bool Active { get; private set; }

        private readonly VisualElement _root;
        private readonly List<VisualElement> _els = new List<VisualElement>();
        private readonly List<IPadTarget> _targets = new List<IPadTarget>();
        private readonly List<PadSlot> _slots = new List<PadSlot>();
        private readonly List<string> _raw = new List<string>();
        private VisualElement _curEl;
        private IPadTarget _cur;
        private string _key;

        public PadFocus(VisualElement root) { _root = root; }

        /// <summary>Ціль у фокусі (null — немає або режим вимкнено).</summary>
        public IPadTarget Current { get { return Active ? _cur : null; } }

        /// <summary>Ключ цілі у фокусі (для кадрів огляду й тестів).</summary>
        public string CurrentKey { get { return _key; } }

        /// <summary>Увімкнути режим без миші й стати на ціль (за ключем, інакше — за замовчуванням), нічого не натискаючи.</summary>
        public void Show()
        {
            Active = true;
            Collect();
            ClearHoverLooks();
            Focus(PadNavMath.Restore(_slots, _key), false);
        }

        /// <summary>Рух миші: режим без миші вимикається, вигляд фокусу знімається, ключ пам'ятається до наступного разу.</summary>
        public void Hide()
        {
            if (_cur != null) _cur.PadHot(false);
            _cur = null; _curEl = null;
            Active = false;
        }

        /// <summary>Хрестовина / стрілки. Перемикач у фокусі (карусель) бере ← → собі.</summary>
        public void Move(PadDir dir)
        {
            if (!Active || _cur == null || !Attached(_curEl)) { Show(); return; }
            if ((dir == PadDir.Left || dir == PadDir.Right) && _cur.PadStep(dir == PadDir.Left ? -1 : 1))
            {
                RingMenuServices.Play(MenuSfx.Toggle);
                return;
            }
            Collect();
            int from = _els.IndexOf(_curEl);
            int to = PadNavMath.Move(_slots, from, dir);
            if (to < 0 || to == from) return;
            Focus(to, true);
        }

        /// <summary>A / Enter: натиснути ціль у фокусі. false — фокусу немає (тоді хост показує його).</summary>
        public bool Activate()
        {
            if (!Active || _cur == null || !Attached(_curEl)) return false;
            if (!_cur.PadEnabled) { RingMenuServices.Play(MenuSfx.Error); return true; }
            RingMenuServices.Play(MenuSfx.Confirm);
            _cur.PadActivate();
            Sync();     // екран, найімовірніше, перебудувався — фокус на ту саму ціль одразу, без порожнього кадру
            return true;
        }

        /// <summary>Кожен кадр: ціль у фокусі зникла з дерева (екран перебудувався) — знайти «ту саму» й показати фокус знову.</summary>
        public void Sync()
        {
            if (!Active) return;
            if (_cur != null && Attached(_curEl)) return;
            Collect();
            Focus(PadNavMath.Restore(_slots, _key), false);
        }

        // ---------------- внутрішнє ----------------

        private void Focus(int i, bool sound)
        {
            if (_cur != null && Attached(_curEl)) _cur.PadHot(false);
            if (i < 0 || i >= _targets.Count) { _cur = null; _curEl = null; return; }
            _cur = _targets[i];
            _curEl = _els[i];
            _key = _slots[i].Key;
            _cur.PadHot(true);
            ScrollIntoView(_curEl);
            if (sound) RingMenuServices.Play(MenuSfx.Move);
        }

        /// <summary>Мишаче наведення, що лишилось на іншій цілі (курсор стоїть), знімається — підсвічено лише фокус.</summary>
        private void ClearHoverLooks()
        {
            for (int i = 0; i < _targets.Count; i++) _targets[i].PadHot(false);
        }

        private void Collect()
        {
            _els.Clear(); _targets.Clear(); _slots.Clear(); _raw.Clear();
            Walk(_root);
            var keys = PadNavMath.UniqueKeys(_raw);
            for (int i = 0; i < _els.Count; i++)
            {
                var r = _els[i].worldBound;
                var t = _targets[i];
                _slots.Add(new PadSlot(keys[i], r.x, r.y, r.width, r.height, t.PadEnabled, t.PadRank));
            }
        }

        private void Walk(VisualElement e)
        {
            if (e == null || IsHidden(e)) return;
            var t = e as IPadTarget ?? PadMark.Of(e);
            if (t != null)
            {
                _els.Add(e); _targets.Add(t); _raw.Add(t.PadKey);
                return;     // цілі в цілі не шукаємо: стрілки каруселі — частина каруселі
            }
            int n = e.childCount;
            for (int i = 0; i < n; i++) Walk(e[i]);
        }

        private static bool IsHidden(VisualElement e)
        {
            return e.style.display.value == DisplayStyle.None;
        }

        private bool Attached(VisualElement e)
        {
            for (var p = e; p != null; p = p.parent)
            {
                if (p == _root) return true;
                if (IsHidden(p)) return false;
            }
            return false;
        }

        /// <summary>Ціль у шухляді нижче краю — список прокручується до неї (з полем), щоб фокус не зникав за краєм.</summary>
        private void ScrollIntoView(VisualElement e)
        {
            for (var p = e.parent; p != null && p != _root; p = p.parent)
            {
                var sv = p as ScrollView;
                if (sv == null) continue;
                var view = sv.worldBound;
                var item = e.worldBound;
                var o = sv.scrollOffset;
                float y = PadNavMath.ScrollToShow(o.y, view.y, view.height, item.y, item.height);
                if (Math.Abs(y - o.y) > 0.5f) sv.scrollOffset = new Vector2(o.x, y);
                return;
            }
        }

        /// <summary>Рамка фокусу для цілі без власного наведення: 2 px кольору тексту по краю, крізь неї клацається.</summary>
        public static VisualElement Frame() { return Frame(RingTheme.Text); }

        /// <summary>Рамка фокусу заданого кольору (на папері афіші — чорнило <c>RingTheme.Bg</c>, бо світла рамка там не видна).</summary>
        public static VisualElement Frame(Color ink)
        {
            var f = new VisualElement();
            f.pickingMode = PickingMode.Ignore;
            f.style.position = Position.Absolute;
            f.style.left = 0f; f.style.top = 0f; f.style.right = 0f; f.style.bottom = 0f;
            f.style.borderTopWidth = 2f; f.style.borderBottomWidth = 2f; f.style.borderLeftWidth = 2f; f.style.borderRightWidth = 2f;
            f.style.borderTopColor = ink; f.style.borderBottomColor = ink;
            f.style.borderLeftColor = ink; f.style.borderRightColor = ink;
            return f;
        }

        /// <summary>Наведення мишею змінює вигляд лише поза режимом без миші (інакше нерухомий курсор перехоплює фокус).</summary>
        public static bool MouseMayHover { get { return !Active; } }
    }
}
