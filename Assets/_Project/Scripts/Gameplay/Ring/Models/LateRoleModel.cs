using System;
using System.Collections.Generic;
using Game.Core.Ring;
using Game.Core.Ring.Career;

namespace Game.Gameplay.Ring.Models
{
    /// <summary>
    /// Вибір пізньої ролі на екрані кінця сезону (TRPG_DESIGN §5.3): рядок на кожну роль — назва, що дає, чи обрана зараз — і «без
    /// пізньої ролі». Кнопка кличе <see cref="Choose"/> (<see cref="CareerSession.ChooseLateRole"/>). Поза кінцем сезону — лише рядок
    /// нинішньої ролі (<see cref="CurrentText"/>) для хабу. Розкладка й кольори — справа екрана («Меню»), показ — через У0.
    /// </summary>
    public sealed class LateRoleModel
    {
        private readonly CareerSession _s;
        private readonly RingText _t;

        public LateRoleModel(CareerSession session, RingText text)
        {
            _s = session ?? throw new ArgumentNullException(nameof(session));
            _t = text ?? throw new ArgumentNullException(nameof(text));
        }

        public sealed class Row
        {
            public LateRole Role;
            public string Name;
            public string Description;
            public bool Selected;
        }

        /// <summary>Показувати блок вибору: кінець сезону (з першого).</summary>
        public bool Visible { get { return _s.CanChooseLateRole; } }

        public string TitleText { get { return _t.Get("c.late.title"); } }
        public string HintText { get { return _t.Get("c.late.hint"); } }

        /// <summary>Три ролі й «без пізньої ролі» — останнім рядком.</summary>
        public List<Row> Rows
        {
            get
            {
                var rows = new List<Row>();
                foreach (var r in LateRoles.All) rows.Add(MakeRow(r));
                rows.Add(MakeRow(LateRole.None));
                return rows;
            }
        }

        private Row MakeRow(LateRole r)
        {
            string key = LateRoles.Key(r);
            string desc = r == LateRole.Icon ? _t.Format(key + ".desc", LateRoles.IconStartHype)
                : r == LateRole.Legend ? _t.Format(key + ".desc", LateRoles.LegendReversalTokens)
                : r == LateRole.OffRingStar ? _t.Format(key + ".desc", LateRoles.StarPromoMoneyStrong, LateRoles.StarPromoMoneyPartial)
                : _t.Get(key + ".desc");
            return new Row { Role = r, Name = _t.Get(key), Description = desc, Selected = _s.State.LateRole == r };
        }

        /// <summary>Рядок хабу «Пізня роль: Ікона»; null — ролі немає.</summary>
        public string CurrentText
        {
            get { return _s.State.LateRole == LateRole.None ? null : _t.Format("c.late.current", _t.Get(LateRoles.Key(_s.State.LateRole))); }
        }

        public bool Choose(LateRole r) { return _s.ChooseLateRole(r); }
    }
}
