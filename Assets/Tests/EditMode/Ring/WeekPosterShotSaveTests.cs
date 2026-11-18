using System;
using System.Collections.Generic;
using System.IO;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Gameplay.Ring.Models;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Сейв огляду «тиждень у федерації 3, афіша з трьома рядками умов» — <c>tools/ring_pm/saves/week_fed3_three_conditions.txt</c>
    /// (ПМ 17.11.2026). Потрібен для кадру У2, який арт-директорка лишила «дивитись на кадрах» (15.11.2026): найвищий букер —
    /// Девід Окоро, федерація 3 — під афішею з найбільшою кількістю рядків умов на вузьких екранах (охоронець
    /// <c>CareerTextFitTests.WeekScene_ClosedDrawer_ShowsTheBookerDownToTheWaist</c> рахує це лише логом). Знімається сценою
    /// <c>-ring-menu-scene week_closed -ring-menu-load &lt;цей файл&gt;</c>.
    /// Охоронець звіряє не рядок зліпка (він росте з кожним новим полем кар'єри), а те, що сейв відновлюється й дає потрібний стан:
    /// старі сейви мають читатися, тож файл не доведеться перегенеровувати щоразу. Змінилась афіша (WeekScreenView.Poster, блок
    /// <c>details</c>) чи розрахунок щаблів суперника — тест скаже, і файл перезаписує <see cref="WriteShotSave"/>.
    /// </summary>
    public sealed class WeekPosterShotSaveTests
    {
        private const int Tier = 3;
        private const string FileName = "week_fed3_three_conditions.txt";

        private static string SavePath()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "tools", "ring_pm", "saves", FileName));
        }

        private static RingText Text()
        {
            return RingText.FromJson(CareerKit.Read("strings_uk.json"), CareerKit.Read("ui_uk.json"), CareerKit.Read("backstage_uk.json"));
        }

        /// <summary>
        /// Рядки умов афіші тижня — ті самі умови й у тому самому порядку, що в <c>WeekScreenView.Poster</c> (блок <c>details</c>):
        /// бонус ажіотажу від промо, реванш, «переріс щабель», нейтральний рядок про щаблі суперника (до росту героя; з ростом
        /// замість нього — наліпка форми), пастка «виключне право», пізня роль.
        /// </summary>
        private static List<string> PosterDetails(CareerSession s, RingText t)
        {
            var c = s.GetShowCard();
            var lines = new List<string>();
            if (c.PromoHypeBonus != 0) lines.Add("promo");
            if (c.IsRematch) lines.Add("rematch");
            if (CareerScreenLines.OutgrownLine(t, c) != null) lines.Add("outgrown");
            if (c.Opponent != null && c.RivalSteps > 0 && s.PlayerGrowth() == 0) lines.Add("steps");
            if (new SeasonOfferModel(s, t).ExclusiveText != null) lines.Add("exclusive");
            if (new LateRoleModel(s, t).CurrentText != null) lines.Add("late");
            return lines;
        }

        /// <summary>Третій сезон, другий тиждень, федерація 3: промо дало ажіотаж на вечір, діє пізня роль «Ікона», герой ще не ріс.</summary>
        private static CareerSession Build()
        {
            var c = CareerKit.Content();
            var s = CareerKit.NewAtCreate(1, c);
            s.CreateHero(CareerKit.Draft(Gimmick.Monster, CareerRole.Hero, RingAttr.Strength, "Новачок"));
            s.State.Season = 3; s.State.Week = 2;
            s.EnterFederation(Tier, c.Federation(Tier).Floor, 2);
            s.State.NextHypeBonus = CareerRules.PromoStrongHype;
            s.State.LateRole = LateRole.Icon; s.State.NextLateRole = LateRole.Icon;
            return s;
        }

        private static void AssertShotState(CareerSession s, string what)
        {
            var t = Text();
            Assert.AreEqual(CareerStage.Week, s.Stage, what + ": екран тижня");
            Assert.AreEqual(Tier, s.State.Federation, what + ": федерація 3");
            Assert.AreEqual("booker3", s.Federation.Booker, what + ": букер федерації 3 — Девід Окоро (BackstageCast, body_m_west_african)");
            var card = s.GetShowCard();
            Assert.IsFalse(card.IsBig || card.IsTitle || card.IsFeud, what + ": звичайне шоу тижня (у великого, титулу й ворожнечі щаблі й рядки інші)");
            Assert.IsNotNull(card.Opponent, what + ": суперник тижня є");
            Assert.Greater(s.GetWeekView().ActionsLeft, 0, what + ": дії тижня ще є — екран саме тижня, не «до вечора»");
            CollectionAssert.AreEqual(new[] { "promo", "steps", "late" }, PosterDetails(s, t), what + ": рівно три рядки умов афіші");
        }

        [Test]
        public void BuiltState_IsFederation3Week_WithThreePosterConditionLines()
        {
            AssertShotState(Build(), "зібраний стан");
        }

        [Test]
        public void CommittedSave_RestoresIntoThatState()
        {
            string path = SavePath();
            Assert.IsTrue(File.Exists(path), "немає сейву огляду " + path + " — запиши його тестом WriteShotSave");
            var lines = File.ReadAllLines(path);
            Assert.AreEqual(2, lines.Length, "сейв огляду: рядок зліпка і рядок «тіло|зачіска», як у грі (RingSaveFile)");
            var s = CareerKit.Fresh(1, CareerKit.Content());
            s.RestoreState(lines[0]);
            AssertShotState(s, "сейв із файлу");
            string[] look = lines[1].Split('|');
            int body, hair;
            Assert.IsTrue(look.Length == 2 && int.TryParse(look[0], out body) && int.TryParse(look[1], out hair), "другий рядок — «тіло|зачіска» числами");
        }

        /// <summary>Перезаписати сейв огляду зі зібраного стану. Не входить у прогін: запускається лише явно, за назвою.</summary>
        [Test, Explicit("перезаписує tools/ring_pm/saves/week_fed3_three_conditions.txt")]
        public void WriteShotSave()
        {
            var s = Build();
            AssertShotState(s, "зібраний стан");
            string path = SavePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, s.CaptureState() + "\n" + "0|0" + "\n", new System.Text.UTF8Encoding(false));
            TestContext.WriteLine("записано " + path);
        }
    }
}
