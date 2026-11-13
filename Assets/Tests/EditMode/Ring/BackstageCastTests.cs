using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Gameplay.Ring;
using Game.Gameplay.Ring.Stage;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Люди кар'єри поза рингом (<see cref="BackstageCast"/>; рішення власника 12.11.2026, № 8 і № 10): кожен, хто говорить у
    /// сцені закулісся, має постать — букер федерації свою, гість події свою. Інакше на його місці стоїть Ліна: так було з
    /// розмовами букерів 2–4, подією найму менеджерки, скаутом і ветераном (знайдено 12.11.2026). Вигляд — тіла й зачіски
    /// набору, кольори лише наявні (Статут вигляду), двох однакових постатей немає.
    /// </summary>
    public class BackstageCastTests
    {
        private static readonly HashSet<string> NotFigures = new HashSet<string> { "player", "narrator", "crowd" };

        private static IEnumerable<string> Speakers(BackstageScene sc)
        {
            return sc.Promo.Nodes.Where(n => n.Type == PromoNodeType.Line && n.Speaker != null).Select(n => n.Speaker).Distinct();
        }

        private static List<BackstageCast.Person> People()
        {
            var all = new List<BackstageCast.Person>(BackstageCast.AllBookers) { BackstageCast.Manager };
            foreach (var g in new[] { "baron", "stalevyi", "host", "scout" }) all.Add(BackstageCast.Guest(g));
            return all;
        }

        [Test]
        public void EveryBookerSpeaker_HasOwnFigure()
        {
            var ids = BackstageCast.AllBookers.Select(p => p.Speaker).ToList();
            int seen = 0;
            foreach (var sc in CareerKit.Content().Backstage.Scenes)
                foreach (var s in Speakers(sc))
                {
                    if (!BackstageCast.IsBooker(s)) continue;
                    seen++;
                    CollectionAssert.Contains(ids, s, sc.Id + ": букер «" + s + "» без своєї постаті — у роздягальні стояла б Ліна");
                }
            Assert.Greater(seen, 0, "у сценах закулісся немає жодного букера — тест нічого не перевірив");
        }

        [Test]
        public void EveryEventGuest_HasFigure_AndStandsRight()
        {
            int checkedGuests = 0;
            foreach (var sc in CareerKit.Content().Backstage.Scenes)
            {
                if (sc.Kind != BackstageKind.Event) continue;
                var guests = Speakers(sc).Where(s => !NotFigures.Contains(s) && !BackstageCast.IsBooker(s)).ToList();
                Assert.LessOrEqual(guests.Count, 1, sc.Id + ": гостей більше одного (" + string.Join(", ", guests) + ") — сцена ставить лише одного");
                string g = BackstageCast.GuestSpeaker(sc.Id);
                if (guests.Count == 0)
                {
                    Assert.IsNull(g, sc.Id + ": у сцені говорить лише букер, а сцена ставить гостя «" + g + "»");
                    continue;
                }
                Assert.AreEqual(guests[0], g, sc.Id + ": гість сцени — той, хто в ній говорить");
                Assert.IsNotNull(BackstageCast.Guest(g), sc.Id + ": немає постаті гостя «" + g + "»");
                if (BackstageCast.GuestWithBooker(sc.Id))
                    Assert.IsTrue(Speakers(sc).Any(BackstageCast.IsBooker), sc.Id + ": гість стоїть поруч із букером, а букер у сцені не говорить");
                checkedGuests++;
            }
            Assert.Greater(checkedGuests, 0, "жодної події з гостем — тест нічого не перевірив");
        }

        [Test]
        public void Figures_UseKitBodies_AndOnlyColorsAlreadyInGame()
        {
            // Палітра костюма героя і кольори, що вже були на постатях (Ліна, Сталевий, ведуча, NPC кар'єри).
            var known = new HashSet<int>(CareerLooks.Costume) { 0x8C9EB3, 0x3B5BA0 };
            foreach (var id in new[] { "hammer", "stalevyi", "dzhura", "vdova", "baron", "komet" }) known.Add(CareerLooks.ForNpc(id).Rgb);
            foreach (var p in People())
            {
                CollectionAssert.Contains(CareerLooks.HeroBodies, p.Body, p.Speaker + ": тіло не з набору");
                Assert.IsTrue(known.Contains(p.Rgb), string.Format("{0}: колір 0x{1:X6} — новий (нових кольорів не вигадуємо)", p.Speaker, p.Rgb));
            }
        }

        [Test]
        public void Figures_AreAllDifferent()
        {
            var looks = People().Select(p => p.Body + "|" + p.Hair + "|" + p.Rgb + "|" + p.Dressed).ToList();
            Assert.AreEqual(looks.Count, looks.Distinct().Count(), "дві постаті однакові: " + string.Join("; ", looks));
        }

        [Test]
        public void Hair_IsKitPieceOfThatSex()
        {
            string manifest = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Assets", "Art", "Characters", "Kit", "kit_manifest.txt");
            var parts = new HashSet<string>(File.ReadAllLines(manifest).Select(l => l.Trim()));
            foreach (var p in People())
                if (!string.IsNullOrEmpty(p.Hair))
                    Assert.IsTrue(parts.Contains((p.Female ? "f:" : "m:") + p.Hair), p.Speaker + ": зачіски «" + p.Hair + "» немає в наборі цієї статі");
        }

        [Test]
        public void UnknownBooker_IsLina()
        {
            Assert.AreEqual(BackstageCast.DefaultBooker, BackstageCast.Booker("booker9").Speaker);
            Assert.AreEqual(BackstageCast.DefaultBooker, BackstageCast.Booker(null).Speaker);
            Assert.IsNull(BackstageCast.GuestSpeaker(null), "розмова з букером — без гостя");
        }
    }
}
