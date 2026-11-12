using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Game.Gameplay.Ring.Models;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Геймпад і фокус без миші на екранах кар'єри — ітерація 2 (власник 11.11.2026: «Геймпад ітерація 2, навіть реальний xbox
    /// підключу.»). У М1 шар вимкнений одним прапорцем <see cref="CareerPadFeature.Enabled"/>: навігації немає, підписи —
    /// «Enter», а захист поля імені (цифри, Пробіл, Enter) працює й без шару.
    /// </summary>
    public class CareerPadFeatureTests
    {
        [Test]
        public void M1_PadLayer_IsOff()
        {
            Assert.IsFalse(CareerPadFeature.Enabled, "фокус без миші й геймпад — ітерація 2; у М1 прапорець вимкнений");
        }

        [Test]
        public void FlagOff_NoNavigation_OldKeysAndEnterHints()
        {
            // екран кар'єри з цілями фокусу, нічого не друкується: стрілки, WASD і хрестовина — старий шлях, фокус не рухають
            Assert.AreEqual(CareerInputRoute.Legacy, CareerPadFeature.Route(false, false, false, true));
            Assert.AreEqual(CareerInputRoute.Legacy, CareerPadFeature.Route(false, false, true));   // поточний прапорець
            Assert.IsFalse(CareerPadFeature.PadOn(true), "екран не бере фокус без миші");
            Assert.IsFalse(CareerPadFeature.PadHints(true), "після кнопки геймпада підпис плит лишається «Enter»");
        }

        [Test]
        public void NameField_Typing_BlocksScreenKeys_WithAndWithoutPadLayer()
        {
            // «1» в імені не міняє ґімік, Пробіл чи Enter не створюють героя — поле друкує, клавіші екрана цього кадру не діють
            foreach (bool padOn in new[] { false, true })
                foreach (bool targets in new[] { false, true })
                    Assert.AreEqual(CareerInputRoute.Typing, CareerPadFeature.Route(padOn, false, true, targets),
                        "поле вводу у фокусі, шар " + (padOn ? "увімкнено" : "вимкнено"));
            Assert.AreEqual(CareerInputRoute.Typing, CareerPadFeature.Route(false, true, true));   // поточний прапорець
        }

        [Test]
        public void MenuScreens_KeepTheirOwnInput()
        {
            foreach (bool padOn in new[] { false, true })
                Assert.AreEqual(CareerInputRoute.Menu, CareerPadFeature.Route(padOn, true, false, false));
        }

        [Test]
        public void Iteration2_FlagOn_RoutesPad_OnlyWhereEveryActionHasATarget()
        {
            Assert.AreEqual(CareerInputRoute.Pad, CareerPadFeature.Route(true, false, false, true));
            Assert.AreEqual(CareerInputRoute.Legacy, CareerPadFeature.Route(true, false, false, false));
        }

        /// <summary>
        /// Прапорець не обходять: екрани не перекривають <c>PadEnabled</c> напряму (лише цілі фокусу), маршрут вводу йде через
        /// <see cref="CareerPadFeature.Route(bool,bool,bool)"/>, а підпис «A» — через <see cref="CareerPadFeature.PadHints"/>.
        /// </summary>
        [Test]
        public void Sources_GoThroughTheFlag()
        {
            string ring = Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Ring");
            var bad = new List<string>();
            bool routed = false, gated = false;
            int padPolls = 0;
            var write = new Regex(@"RingMenuInput\.PadLast\s*=\s*(true|false)\s*;");
            foreach (var file in Directory.GetFiles(ring, "*.cs", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(file);
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string l = lines[i];
                    string at = name + ":" + (i + 1);
                    if (l.Contains("override bool PadEnabled")) bad.Add(at + " — екран перекриває PadEnabled повз прапорець");
                    if (l.Contains("CareerPadFeature.Route(")) routed |= name == "RingGameBootstrap.Menus.cs";
                    if (l.Contains("CareerPadFeature.PadOn(")) gated |= name == "RingUi.cs";
                    // опитування фокуса без миші — один виклик, і той у гілці маршруту Pad (прапорець не обійти прямим викликом)
                    if (l.Contains("PollCareerPad(") && !l.Contains("bool PollCareerPad("))
                    {
                        padPolls++;
                        if (!l.Contains("CareerInputRoute.Pad")) bad.Add(at + " — PollCareerPad повз маршрут CareerPadFeature.Route");
                    }
                    // читання PadLast: лише в RingMenuInput, у PollCareerPad (за маршрутом Pad: _padLastSeen) і через PadHints
                    bool read = l.Contains("PadLast") && !write.IsMatch(l) && name != "RingMenuInput.cs"
                        && !(name == "RingGameBootstrap.Menus.cs" && l.Contains("_padLastSeen"));
                    if (read && !l.Contains("CareerPadFeature.PadHints(")) bad.Add(at + " — підпис геймпада повз CareerPadFeature.PadHints");
                }
            }
            if (!routed) bad.Add("RingGameBootstrap.Menus.cs: маршрут вводу не через CareerPadFeature.Route");
            if (!gated) bad.Add("RingUi.cs: RingScreenView.PadEnabled не через CareerPadFeature.PadOn");
            if (padPolls != 1) bad.Add("PollCareerPad: викликів " + padPolls + ", має бути рівно один — у гілці CareerInputRoute.Pad");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        /// <summary>
        /// Наведення мишею на цілі кар'єри — лише через <c>PadFocus.Hover</c> (аудит P3, 11.11.2026): інакше, коли гравець знову
        /// бере мишу, елемент під нерухомим курсором лишався б без підсвіту; а екран без фокуса без миші знімає режим
        /// (<c>PadFocus.Release</c>), щоб наведення там діяло.
        /// </summary>
        [Test]
        public void Sources_MouseHover_GoesThroughPadFocus()
        {
            string ring = Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Ring");
            var bad = new List<string>();
            bool released = false;
            // екрани з цілями фокусу (PadTargets) і їхні складники: кнопки RingUi, CareerScreenView, плитки й плити «Арени»
            var career = new HashSet<string> { "RingUi.cs", "CareerScreens.cs", "EndingScreenView.cs", "FeudScreenView.cs", "PromoScreenView.cs" };
            foreach (var file in Directory.GetFiles(ring, "*.cs", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(file);
                bool careerUi = career.Contains(name) || (file.Replace('\\', '/').Contains("/UI/Career/") && name != "PadFocus.cs");
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string code = lines[i].Trim();
                    if (code.StartsWith("//")) continue;
                    if (name == "RingGameBootstrap.Menus.cs" && code.StartsWith("if (route == CareerInputRoute.Menu || route == CareerInputRoute.Legacy) PadFocus.Release();"))
                        released = true;
                    if (name != "PadFocus.cs" && code.Contains("MouseMayHover"))
                        bad.Add(name + ":" + (i + 1) + " — наведення повз PadFocus.Hover");
                    if (careerUi && (code.Contains("PointerEnterEvent") || code.Contains("PointerLeaveEvent")))
                        bad.Add(name + ":" + (i + 1) + " — сире наведення на екрані кар'єри (лише PadFocus.Hover)");
                }
            }
            if (!released) bad.Add("RingGameBootstrap.Menus.cs: маршрути «меню» й «старі клавіші» не знімають режим (PadFocus.Release)");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }
    }
}
