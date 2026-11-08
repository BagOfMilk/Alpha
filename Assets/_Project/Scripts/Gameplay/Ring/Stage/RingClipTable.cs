using System.Collections.Generic;
using Game.Core.Ring;
using Game.Core.Ring.Match;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Яким планом знімати крок: загальним, ближче (удар) чи кіно (кидок, фінішер, утримання). <see cref="Cine"/> — кіноплан,
    /// який режисер рахує сам з поз борців (фінішер, перемога): склейкою, а не наїздом.
    /// </summary>
    public enum StageCam { Overview = 0, Close = 1, Big = 2, Low = 3, Cine = 4 }

    public enum StageStepKind
    {
        /// <summary>Парний кліп: A і B за зсувом із маніфесту (<see cref="StageStep.Clip"/> + _A/_B).</summary>
        Pair = 0,
        /// <summary>Один борець грає кліп на місці.</summary>
        Solo,
        /// <summary>Борець іде в нову зону (ходьба).</summary>
        Walk,
        /// <summary>Лише рефері (попередження, відлік).</summary>
        Ref,
        /// <summary>Кінець матчу: рефері піднімає руку переможцю.</summary>
        End,
        /// <summary>
        /// Кидок у канати чи в кут: пара <see cref="StageStep.Clip"/> (Whip_*), далі кинутий (B) біжить сам — бігом
        /// <see cref="StageStep.Run"/> до каната й удар спиною (<see cref="StageStep.Rope1"/>), за потреби відскок і ще канат
        /// (<see cref="StageStep.Rope2"/>). Лінія каната кліпа лягає на канат рингу вздовж осі.
        /// </summary>
        Whip
    }

    /// <summary>Один крок режисури: що зіграти й ким. Чисті дані — без рушія, тож таблицю «дія → кліп» видно окремо.</summary>
    public sealed class StageStep
    {
        public StageStepKind Kind;
        /// <summary>Хто виконує (A парного кліпу, сольний борець).</summary>
        public MatchSide Actor;
        /// <summary>База імені парного кліпу (без _A/_B) або ім'я сольного.</summary>
        public string Clip;
        /// <summary>Кліп рефері, що йде разом із кроком (або єдиний у кроку Ref).</summary>
        public string RefClip;
        public StageCam Cam;
        /// <summary>Скільки тримати кадр, якщо кліпів нема, с.</summary>
        public float Hold;
        /// <summary>Кліп грається один раз, без петлі (для петльових Idle_*).</summary>
        public bool Once;
        /// <summary>Рефері дивиться на цього борця (попередження).</summary>
        public bool RefFacesActor;
        public string Note;
        /// <summary>Кидок: цикл бігу кинутого між кліпами (вставка до каната).</summary>
        public string Run;
        /// <summary>Кидок у канати: перший канат (удар спиною) і, якщо є, другий — після відскоку, через ринг.</summary>
        public string Rope1, Rope2;
        /// <summary>Кидок у кут (кліп Whip_Corner_* сам доводить до кута).</summary>
        public bool Corner;
        /// <summary>Крок-наслідок, а не сама дія події (суперник підвівся, рефері попереджає): такт на ньому не ставиться.</summary>
        public bool Aside;

        public static StageStep Pair(MatchSide a, string baseName, StageCam cam, string refClip = null, string note = null)
        {
            return new StageStep { Kind = StageStepKind.Pair, Actor = a, Clip = baseName, Cam = cam, RefClip = refClip, Note = note };
        }

        public static StageStep Solo(MatchSide who, string clip, StageCam cam, bool once = false, string note = null)
        {
            return new StageStep { Kind = StageStepKind.Solo, Actor = who, Clip = clip, Cam = cam, Once = once, Note = note };
        }

        /// <summary>Кидок: <paramref name="whipper"/> кидає суперника (B кліпа) у канати чи в кут.</summary>
        public static StageStep Whip(MatchSide whipper, string whip, string run, string rope1, string rope2, bool corner, StageCam cam, string note)
        {
            return new StageStep { Kind = StageStepKind.Whip, Actor = whipper, Clip = whip, Run = run, Rope1 = rope1, Rope2 = rope2, Corner = corner, Cam = cam, Note = note };
        }

        /// <summary>Усі кліпи кроку (для перевірки, що вони є в маніфесті): пара — _A і _B, кидок — ще біг і канати.</summary>
        public IEnumerable<string> AllClips()
        {
            switch (Kind)
            {
                case StageStepKind.Pair:
                case StageStepKind.Whip:
                    yield return Clip + "_A"; yield return Clip + "_B";
                    break;
                case StageStepKind.Solo:
                case StageStepKind.Walk:
                    yield return Clip;
                    break;
            }
            if (Run != null) yield return Run;
            if (Rope1 != null) yield return Rope1;
            if (Rope2 != null) yield return Rope2;
            if (RefClip != null) yield return RefClip;
        }
    }

    /// <summary>
    /// Таблиця «подія матчу → кроки режисури» (gdd §8): дію й ступінь кидка перекладає на кліпи рингу. Чиста функція;
    /// стан зон і поз режисер веде окремо з ефектів подій. Дії без власного кліпа (больовий, брудний, відчайдушний,
    /// фінішер, напарник, летючий) узято найближчим наявним — див. Note.
    /// </summary>
    public static class RingClipTable
    {
        public static MatchSide Opp(MatchSide s) { return s == MatchSide.Player ? MatchSide.Rival : MatchSide.Player; }

        public static List<StageStep> Plan(MatchEvent ev)
        {
            var list = new List<StageStep>();
            MatchSide a = ev.Actor, b = Opp(a);
            RollTier t = ev.Tier;
            switch (ev.Kind)
            {
                case MatchEventKind.Action:
                    PlanAction(list, ev, a, b, t);
                    break;
                case MatchEventKind.Reverse:
                    // Реверс: захисник (Actor) відповідає. 7-9 — обмін захватами, 10+ — кидає нападника.
                    if (t == RollTier.Strong) list.Add(StageStep.Pair(a, "Bodyslam", StageCam.Big, null, "реверс: кидок"));
                    else if (t == RollTier.Partial) list.Add(StageStep.Pair(a, "Lockup_Exchange", StageCam.Close, null, "реверс: обмін захватами"));
                    break;
                case MatchEventKind.Kickout:
                    // Утримання: нападник накриває захисника (Actor події), рефері рахує. Провал — повні три.
                    list.Add(StageStep.Pair(b, "Cover", StageCam.Big, t == RollTier.Miss ? "Ref_Count_Pin_3" : "Ref_Count_Pin_2",
                        "утримання (Cover + відлік рефері)"));
                    break;
                case MatchEventKind.Endure:
                    list.Add(new StageStep { Kind = StageStepKind.Ref, RefClip = "Ref_Ask_Submit_Loop", Hold = 1.6f, Cam = StageCam.Close, Once = true,
                        Note = "рефері питає про здачу" });
                    break;
                case MatchEventKind.Rescue:
                    list.Add(new StageStep { Kind = StageStepKind.Ref, RefClip = "Ref_Flinch", Hold = 1.3f, Cam = StageCam.Overview, Note = "напарник відволікає рефері" });
                    break;
                case MatchEventKind.MatchEnd:
                    list.Add(new StageStep { Kind = StageStepKind.End, Cam = StageCam.Big, Note = "кінець: рефері піднімає руку" });
                    break;
            }
            // Попередження борцеві — рефері реагує окремим кроком.
            if (ev.Kind == MatchEventKind.Action)
            {
                for (int i = 0; i < ev.Effects.Count; i++)
                {
                    if (ev.Effects[i].Kind != MatchEffectKind.Warning) continue;
                    list.Add(new StageStep { Kind = StageStepKind.Ref, Actor = ev.Effects[i].Who, RefFacesActor = true, RefClip = "Ref_Warn", Cam = StageCam.Close, Note = "попередження", Aside = true });
                    break;
                }
            }
            return list;
        }

        private static void PlanAction(List<StageStep> list, MatchEvent ev, MatchSide a, MatchSide b, RollTier t)
        {
            bool miss = t == RollTier.Miss, strong = t == RollTier.Strong;
            bool stoodUp = false;
            for (int i = 0; i < ev.Effects.Count; i++)
                if (ev.Effects[i].Kind == MatchEffectKind.Stand && ev.Effects[i].Who == b) stoodUp = true;

            switch (ev.Action)
            {
                case MatchActionId.Strike:
                    list.Add(StageStep.Pair(a, miss ? "Center_Sidestep" : strong ? "Center_BigBoot_Stagger" : "Forearm_Strike",
                        strong ? StageCam.Big : StageCam.Close, null, miss ? "промах: суперник ухиляється" : null));
                    break;
                case MatchActionId.Throw:
                    if (miss) list.Add(StageStep.Solo(a, "Bump_Back", StageCam.Close, false, "кидок не вдався: падає сам"));
                    else list.Add(StageStep.Pair(a, "Bodyslam", StageCam.Big));
                    break;
                case MatchActionId.IntoRopes:
                    // Огляд 08.11.2026 (пункт 8): у матчі не було ні бігу, ні канатів. Кидок у канати, біг і удар спиною в канат
                    // (відскок — завжди спиною до канатів, власник 30.10.2026); чисто — відскок і через ринг у протилежні канати.
                    // Бік кидка (повз праве чи ліве плече) чергується. Промах — обережний захват, як і раніше.
                    if (miss) list.Add(StageStep.Pair(a, "Lockup_Cautious", StageCam.Close, null, "у канати не вийшло: обережний захват"));
                    else if (strong) list.Add(StageStep.Whip(a, "Whip_Ropes_Fast", "Run_Loop", "Rope_Back_L", "Rope_Back_Stop", false, StageCam.Close,
                        "у канати з відскоком: біг, спина в канат, відскок і через ринг у протилежні канати"));
                    else list.Add(StageStep.Whip(a, ev.Seq % 2 == 0 ? "Whip_Ropes" : "Whip_Ropes_Left", "Run_Jog_Loop", "Rope_Back_Stop", null, false, StageCam.Close,
                        "у канати: біг і спина в канат, зупинився на канатах"));
                    break;
                case MatchActionId.IntoCorner:
                    // Кидок у кут; промах — реверс: кидають самого нападника (ядро ставить у кут його).
                    if (miss) list.Add(StageStep.Whip(b, "Whip_Corner", null, null, null, true, StageCam.Close, "у кут не вийшло: реверс, у кут летить нападник"));
                    else list.Add(StageStep.Whip(a, strong ? "Whip_Corner_Fast" : "Whip_Corner", null, null, null, true, StageCam.Close, "кидок у кут"));
                    break;
                case MatchActionId.TopRope:
                    list.Add(StageStep.Solo(a, "Move_Corner_Enter", StageCam.Overview, false, "бракує кліпа «з третього каната»"));
                    if (miss) list.Add(StageStep.Solo(a, "Bump_Back", StageCam.Close));
                    else list.Add(StageStep.Pair(a, "Center_ShoulderTackle", StageCam.Big, null, "летючий — найближчий таран"));
                    break;
                case MatchActionId.Taunt:
                    list.Add(StageStep.Solo(a, miss ? "Idle_Hurt_Body_Loop" : "Idle_Hype_Loop", StageCam.Close, true));
                    if (stoodUp) { var up = StageStep.Solo(b, "GetUp_Back", StageCam.Overview); up.Aside = true; list.Add(up); }
                    break;
                case MatchActionId.Dirty:
                    list.Add(StageStep.Pair(a, miss ? "Center_Sidestep" : "Center_Headbutt", StageCam.Close, null, "брудний прийом: удар головою"));
                    break;
                case MatchActionId.Submission:
                    list.Add(StageStep.Pair(a, miss ? "Lockup_Stalemate" : strong ? "Chain_Headlock_Crank" : "Chain_Headlock", StageCam.Close,
                        null, "бракує больового з реальною підсадкою"));
                    break;
                case MatchActionId.Finisher:
                    if (miss) list.Add(StageStep.Solo(a, "Bump_Back", StageCam.Big, false, "фінішер не вдався"));
                    else list.Add(StageStep.Pair(a, "Center_Lariat_Heavy", StageCam.Big, null, "бракує підписного фінішера — важкий ларіат"));
                    break;
                case MatchActionId.Desperate:
                    if (miss) list.Add(StageStep.Solo(a, "Bump_Back", StageCam.Close));
                    else list.Add(StageStep.Pair(a, strong ? "Center_ShoulderBlock_Heavy" : "Center_ShoulderBlock", StageCam.Big, null, "відчайдушний — важкий таран"));
                    break;
                case MatchActionId.GetUp:
                    list.Add(miss ? StageStep.Solo(a, "Idle_Tired_Loop", StageCam.Overview, true) : StageStep.Solo(a, "GetUp_Back", StageCam.Overview));
                    break;
                case MatchActionId.Pin:
                    break; // сам відлік — у події Kickout, що йде слідом
                case MatchActionId.LeaveRing:
                case MatchActionId.ReturnToRing:
                    list.Add(new StageStep { Kind = StageStepKind.Walk, Actor = a, Clip = "Move_Walk_Fwd_Loop", Cam = StageCam.Overview, Note = "вихід/повернення — ходьба, бракує перелазу через канати" });
                    break;
                case MatchActionId.Recover:
                    list.Add(StageStep.Solo(a, "Idle_Tired_Loop", StageCam.Overview, true));
                    break;
                case MatchActionId.Ally:
                    list.Add(new StageStep { Kind = StageStepKind.Ref, RefClip = "Ref_Point_Ropes", Hold = 1.4f, Cam = StageCam.Overview, Note = "бракує виходу напарника" });
                    break;
            }
        }
    }
}
