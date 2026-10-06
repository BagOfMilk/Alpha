using System;
using System.Collections.Generic;
using Game.Core.Session;
using Game.Gameplay.Text;
using UnityEngine;

namespace Game.Gameplay.UI
{
    /// <summary>Що рендер панелі просить в оболонки: виконати дію, повести до місця, закрити панель, домалювати особливе.</summary>
    public interface IUxRenderHost
    {
        SessionState State { get; }
        bool Female { get; }
        UxInlineRefusals Refusals { get; }
        void RunAction(UxAction action);
        void Link(string placeId);
        void ClosePanel();
        /// <summary>Особливе під картками панелі (полонені на Вічі — панелі треку бою).</summary>
    }

    /// <summary>
    /// Тонкий рендер моделі панелі на IMGUI (docs/UX_DESIGN.md §3.3, §6):
    /// рамка ліворуч, заголовок і «Закрити», до чотирьох підвкладок, картки
    /// з чипами, рядками, позначками стадій і кнопками дій. Недоступна дія
    /// показує причину рядком (UI-03), відмова ядра — рядком під кнопкою
    /// (UX-11). Таблиці як примітиву немає. Модель не знає про рендер: коли
    /// екрани переїдуть на UI Toolkit, переписується лише цей файл.
    /// </summary>
    public sealed class UxPanelView
    {
        private Vector2 _scroll;
        private UxPanelId _lastPanel;
        private string _lastContext;
        private readonly Dictionary<UxPanelId, string> _section = new Dictionary<UxPanelId, string>();

        private GUIStyle _wrapBody;
        private GUIStyle _wrapHint;
        private GUIStyle _wrapDanger;

        public void Draw(Rect area, UxPanelModel model, string context, IUxRenderHost host, Game.Core.Characters.Creation.Gender g)
        {
            if (model == null) return;
            EnsureStyles();
            if (model.Id != _lastPanel || context != _lastContext)
            {
                _scroll = Vector2.zero;
                _lastPanel = model.Id;
                _lastContext = context;
            }

            Widgets.SolidRect(area, AlphaSkin.BgPanel);
            GUILayout.BeginArea(area);
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandHeight(true));

            GUILayout.BeginHorizontal();
            GUILayout.Label(model.Title ?? string.Empty, AlphaSkin.Header, GUILayout.ExpandWidth(true));
            if (Widgets.SecondaryButton(UkrainianText.Get("ux.common.close", g), GUILayout.ExpandWidth(false)))
                host.ClosePanel();
            GUILayout.EndHorizontal();

            var sections = Sections(model);
            string current = null;
            if (sections.Count > 1)
            {
                string remembered;
                current = _section.TryGetValue(model.Id, out remembered) && sections.Contains(remembered) ? remembered : sections[0];
                float available = area.width - 60f;
                Flow(sections, available, s => Widgets.TabButtonWidth(s), s =>
                {
                    if (Widgets.CompactTabButton(s, s == current))
                    {
                        _section[model.Id] = s;
                        _scroll = Vector2.zero;
                    }
                });
            }

            _scroll = Widgets.ScrollListBegin(_scroll, GUILayout.ExpandHeight(true));
            int shown = 0;
            float cardWidth = area.width - 90f;
            foreach (var card in model.Cards)
            {
                if (current != null && card.Section != current) continue;
                DrawCard(card, cardWidth, host, g);
                shown++;
            }
            if (shown == 0 && !string.IsNullOrEmpty(model.EmptyText))
                GUILayout.Label(model.EmptyText, _wrapHint);
            Widgets.ScrollListEnd();

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private void DrawCard(UxCard card, float width, IUxRenderHost host, Game.Core.Characters.Creation.Gender g)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            if (!string.IsNullOrEmpty(card.Title)) GUILayout.Label(card.Title, AlphaSkin.SubHeader);
            if (!string.IsNullOrEmpty(card.Subtitle)) GUILayout.Label(card.Subtitle, _wrapHint);

            if (card.Chips.Count > 0)
                Flow(card.Chips, width, c => Widgets.BadgeWidth(c.Text), c => Widgets.Badge(c.Text, Tint(c.Tone)));

            foreach (var line in card.Lines)
                if (!string.IsNullOrEmpty(line)) GUILayout.Label(line, _wrapBody);

            if (card.PipsTotal > 0) Widgets.ProgressPips(card.PipsFilled, card.PipsTotal);

            if (!string.IsNullOrEmpty(card.LinkPlaceId))
            {
                string placeId = card.LinkPlaceId;
                if (Widgets.SecondaryButton(UkrainianText.Get("ux.common.show_in_village", g), GUILayout.ExpandWidth(false)))
                    host.Link(placeId);
            }

            if (card.Actions.Count > 0)
            {
                var reasons = new List<string>();
                Flow(card.Actions, width, a => Widgets.TabButtonWidth(Label(a)), a =>
                {
                    string label = Label(a);
                    string reason = a.ReasonIn(host.State, host.Female);
                    if (reason != null)
                    {
                        Widgets.DisabledButton(label, null, GUILayout.ExpandWidth(false));
                        string line = UkrainianText.Format("ux.common.reason_line", g, "action", a.Label, "reason", reason);
                        if (!reasons.Contains(line)) reasons.Add(line);
                        return;
                    }
                    bool clicked;
                    if (a.Selected) clicked = Widgets.TabButton(label, true, GUILayout.ExpandWidth(false));
                    else if (a.Intent == UxIntent.Primary) clicked = Widgets.PrimaryButton(label, GUILayout.ExpandWidth(false));
                    else if (a.Intent == UxIntent.Danger) clicked = Widgets.DangerButton(label, GUILayout.ExpandWidth(false));
                    else clicked = Widgets.SecondaryButton(label, GUILayout.ExpandWidth(false));
                    if (clicked) host.RunAction(a);
                });
                foreach (var r in reasons) GUILayout.Label(r, _wrapHint);
                foreach (var a in card.Actions)
                {
                    string refusal = host.Refusals.For(a.Id);
                    if (!string.IsNullOrEmpty(refusal)) GUILayout.Label(refusal, _wrapDanger);
                }
            }
            GUILayout.EndVertical();
        }

        /// <summary>Підпис кнопки: дієслово першим, ціна чипом поруч (UI-02).</summary>
        private static string Label(UxAction a)
        {
            if (a.Chips.Count == 0) return a.Label ?? string.Empty;
            var parts = new List<string>();
            foreach (var c in a.Chips) parts.Add(c.Text);
            return a.Label + " · " + string.Join(" · ", parts);
        }

        private static List<string> Sections(UxPanelModel model)
        {
            var list = new List<string>();
            foreach (var c in model.Cards)
                if (!string.IsNullOrEmpty(c.Section) && !list.Contains(c.Section)) list.Add(c.Section);
            return list;
        }

        /// <summary>Рядок елементів, що переноситься, коли не влазить у ширину (IMGUI сам не переносить).</summary>
        private static void Flow<T>(IList<T> items, float available, Func<T, float> width, Action<T> draw)
        {
            float row = 0f;
            GUILayout.BeginHorizontal();
            for (int i = 0; i < items.Count; i++)
            {
                float w = width(items[i]) + 6f;
                if (row > 0f && row + w > available)
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    row = 0f;
                }
                row += w;
                draw(items[i]);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        /// <summary>Сенс кольору → токен палітри HUD (UI-01): модель знає лише сенс.</summary>
        private static Color32 Tint(UxTone tone)
        {
            switch (tone)
            {
                case UxTone.Good: return new Color32(58, 96, 52, 255);
                case UxTone.Bad: return AlphaSkin.Danger;
                case UxTone.Threat: return AlphaSkin.AccentActive;
                case UxTone.Own: return AlphaSkin.BattlePlayerSideMuted;
                default: return AlphaSkin.BgRaised;
            }
        }

        private void EnsureStyles()
        {
            if (_wrapBody != null) return;
            _wrapBody = new GUIStyle(AlphaSkin.Body) { wordWrap = true };
            _wrapHint = new GUIStyle(AlphaSkin.Tooltip) { wordWrap = true };
            _wrapDanger = new GUIStyle(AlphaSkin.DangerText) { wordWrap = true };
        }
    }
}
