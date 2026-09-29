using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Game.Core.Prisoners
{
    /// <summary>
    /// Прихильність полоненого (Поправка №14.2, інваріант 6):
    /// ЯКІР — наскільки полонений готовий перейти до громади;
    /// СПОЖИВАЧ — «переманити» на віче доступне лише з полоси <see cref="Ready"/>;
    /// СИГНАЛ — зміна полоси пишеться у стрічку (<c>prisoner.disposition</c>).
    /// Число — internal; назовні лише полоса.
    /// </summary>
    public enum PrisonerDisposition { Hostile = 0, Wavering = 1, Ready = 2 }

    /// <summary>
    /// Неспокій полоненого (Поправка №14.2, інваріант 6):
    /// ЯКІР — скільки лишилось до втечі; СПОЖИВАЧ — на повній шкалі полонений тікає;
    /// СИГНАЛ — зміна полоси і сама втеча — у стрічку.
    /// </summary>
    public enum PrisonerRestlessness { Calm = 0, Restless = 1 }

    /// <summary>Один полонений: хто, коли взятий, і дві приховані шкали.</summary>
    public sealed class Prisoner
    {
        public string Id;
        public string EnemyDefinitionId;
        public string DisplayName;
        /// <summary>0 — рядовий, 1 — міні-бос (боси в полон не йдуть).</summary>
        public int Rank;
        /// <summary>Правило кастингу №12.9: персонажів із російських першоджерел переманити не можна.</summary>
        public bool NeverRecruitable;
        public int DayTaken;

        internal int Disposition;   // 0..100
        internal int EscapeRisk;    // 0..100

        public PrisonerDisposition DispositionBand =>
            Disposition >= 100 ? PrisonerDisposition.Ready : Disposition >= 34 ? PrisonerDisposition.Wavering : PrisonerDisposition.Hostile;

        public PrisonerRestlessness RestlessnessBand =>
            EscapeRisk >= 50 ? PrisonerRestlessness.Restless : PrisonerRestlessness.Calm;
    }

    /// <summary>Що сталося з полоненим за добу — ключ для стрічки і сам полонений.</summary>
    public sealed class PrisonerEvent
    {
        /// <summary>"disposition" | "restless" | "escaped" | "hungry".</summary>
        public string Kind;
        public Prisoner Prisoner;
    }

    /// <summary>Умови доби для полонених: хто вмовляє, чи є варта, чи вистачає їжі.</summary>
    public readonly struct PrisonerDayInputs
    {
        public readonly int Day;
        /// <summary>Найкраще Переконання серед присутніх у громаді.</summary>
        public readonly int BestPersuade;
        /// <summary>Збудована Сторожа (№12.9) — варта стримує втечу.</summary>
        public readonly bool Guarded;
        /// <summary>Їжі вистачило на всіх полонених.</summary>
        public readonly bool Fed;

        public PrisonerDayInputs(int day, int bestPersuade, bool guarded, bool fed)
        {
            Day = day;
            BestPersuade = bestPersuade;
            Guarded = guarded;
            Fed = fed;
        }
    }

    /// <summary>Числа полону — ПЛЕЙСХОЛДЕРИ (Поправка №14.11).</summary>
    public sealed class PrisonerBalance
    {
        /// <summary>Переконання, з якого вмовляння йде повним кроком.</summary>
        public int RecruitPersuade = 4;
        public int DispositionPerDay = 25;
        public int DispositionPerDayWeak = 8;
        public int HungerDispositionLoss = 10;
        public int EscapePerDay = 20;
        public int EscapePerDayGuarded = 6;
        public int RansomGrunt = 10;
        public int RansomMiniBoss = 25;
    }

    /// <summary>
    /// Полонені громади (Поправка №14.2): щодня їдять, вмовляються і шукають
    /// нагоди втекти. Детерміновано — за умовами доби, без кидка (інваріант 1).
    /// Долю вирішує віче (<c>GameSession</c>): переманити, викуп, відпустити.
    /// </summary>
    public sealed class PrisonerLedger
    {
        private readonly List<Prisoner> _prisoners = new List<Prisoner>();
        private int _seq;

        public PrisonerBalance Balance { get; } = new PrisonerBalance();
        public IReadOnlyList<Prisoner> All => _prisoners;

        public Prisoner Get(string id)
        {
            foreach (var p in _prisoners) if (p.Id == id) return p;
            return null;
        }

        public Prisoner Take(string enemyDefinitionId, string displayName, int rank, bool neverRecruitable, int day)
        {
            var p = new Prisoner
            {
                Id = "prisoner#" + _seq++,
                EnemyDefinitionId = enemyDefinitionId,
                DisplayName = displayName,
                Rank = rank,
                NeverRecruitable = neverRecruitable,
                DayTaken = day
            };
            _prisoners.Add(p);
            return p;
        }

        public bool Remove(string id)
        {
            var p = Get(id);
            return p != null && _prisoners.Remove(p);
        }

        public int RansomFor(Prisoner p) => p.Rank >= 1 ? Balance.RansomMiniBoss : Balance.RansomGrunt;

        /// <summary>
        /// Доба полону: вмовляння (повним кроком — якщо є хто з Переконанням ≥ порога),
        /// голод (прихильність падає), неспокій (варта стримує). Втеча — на повній
        /// шкалі. Кожна зміна полоси — подія (інваріант 4).
        /// </summary>
        public List<PrisonerEvent> Tick(PrisonerDayInputs day)
        {
            var events = new List<PrisonerEvent>();
            for (int i = _prisoners.Count - 1; i >= 0; i--)
            {
                var p = _prisoners[i];
                var dispBefore = p.DispositionBand;
                var restBefore = p.RestlessnessBand;

                if (!day.Fed)
                {
                    p.Disposition = Math.Max(0, p.Disposition - Balance.HungerDispositionLoss);
                    events.Add(new PrisonerEvent { Kind = "hungry", Prisoner = p });
                }
                else
                {
                    int step = day.BestPersuade >= Balance.RecruitPersuade ? Balance.DispositionPerDay : Balance.DispositionPerDayWeak;
                    p.Disposition = Math.Min(100, p.Disposition + step);
                }

                p.EscapeRisk = Math.Min(100, p.EscapeRisk + (day.Guarded ? Balance.EscapePerDayGuarded : Balance.EscapePerDay));

                if (p.EscapeRisk >= 100)
                {
                    _prisoners.RemoveAt(i);
                    events.Add(new PrisonerEvent { Kind = "escaped", Prisoner = p });
                    continue;
                }
                if (p.DispositionBand != dispBefore) events.Add(new PrisonerEvent { Kind = "disposition", Prisoner = p });
                if (p.RestlessnessBand != restBefore) events.Add(new PrisonerEvent { Kind = "restless", Prisoner = p });
            }
            events.Reverse(); // порядок взяття в полон
            return events;
        }

        // ---- зліпок: без «;» і «=» (роздільники заголовка сейву) ----

        public string CaptureState()
        {
            var sb = new StringBuilder();
            sb.Append(_seq.ToString(CultureInfo.InvariantCulture));
            foreach (var p in _prisoners)
            {
                sb.Append('|').Append(p.Id).Append(',').Append(p.EnemyDefinitionId).Append(',')
                  .Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(p.DisplayName ?? string.Empty)).Replace('=', '~')).Append(',')
                  .Append(I(p.Rank)).Append(',').Append(p.NeverRecruitable ? '1' : '0').Append(',')
                  .Append(I(p.DayTaken)).Append(',').Append(I(p.Disposition)).Append(',').Append(I(p.EscapeRisk));
            }
            return sb.ToString();
        }

        public void RestoreState(string blob)
        {
            _prisoners.Clear();
            _seq = 0;
            if (string.IsNullOrEmpty(blob)) return;
            var parts = blob.Split('|');
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _seq);
            for (int i = 1; i < parts.Length; i++)
            {
                var f = parts[i].Split(',');
                if (f.Length < 8) continue;
                _prisoners.Add(new Prisoner
                {
                    Id = f[0],
                    EnemyDefinitionId = f[1],
                    DisplayName = Encoding.UTF8.GetString(Convert.FromBase64String(f[2].Replace('~', '='))),
                    Rank = P(f[3]),
                    NeverRecruitable = f[4] == "1",
                    DayTaken = P(f[5]),
                    Disposition = P(f[6]),
                    EscapeRisk = P(f[7])
                });
            }
        }

        private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

        private static int P(string s)
        {
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v);
            return v;
        }
    }
}
