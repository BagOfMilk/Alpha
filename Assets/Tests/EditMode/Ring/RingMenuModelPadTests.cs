using System.Collections.Generic;
using Game.Gameplay.Ring.Models;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Фокус без миші на екранах кар'єри (P3 геймпад, 11.11.2026, <see cref="PadNavMath"/>): куди стати першим, куди
    /// повернутись після перебудови екрана, куди перейти хрестовиною, як прокрутити шухляду до цілі.
    /// Розкладки — спрощені копії справжніх: ряд плиток тижня 16:9, два ряди на 4:3, шухляда зі списком.
    /// </summary>
    public class RingMenuModelPadTests
    {
        private static PadSlot S(string key, float x, float y, float w = 180f, float h = 120f, bool on = true, int rank = 3)
        {
            return new PadSlot(key, x, y, w, h, on, rank);
        }

        /// <summary>Тиждень 16:9: шість плиток в один ряд (ранг 1), над ними дві вкладки шухляди, праворуч унизу золота плита.</summary>
        private static List<PadSlot> Week()
        {
            var s = new List<PadSlot>();
            for (int i = 0; i < 6; i++) s.Add(S("tile:" + i, 40f + i * 190f, 600f, 180f, 130f, true, 1));
            s.Add(S("tab:dev", 1000f, 540f, 150f, 40f));
            s.Add(S("tab:shop", 1160f, 540f, 150f, 40f));
            s.Add(S("gold", 1220f, 760f, 340f, 90f, true, 2));
            return s;
        }

        [Test]
        public void Default_PrefersLowestRankEnabled_ThenTreeOrder()
        {
            var w = Week();
            Assert.AreEqual(0, PadNavMath.Default(w), "тиждень — перша плитка дії, не «Перейти до вечора»");

            var tiles = new List<PadSlot> { S("a", 0, 0, 10, 10, false, 1), S("b", 20, 0, 10, 10, true, 1), S("gold", 40, 0, 10, 10, true, 2) };
            Assert.AreEqual(1, PadNavMath.Default(tiles), "недоступна плитка не перша, якщо є доступна того ж рангу");

            var off = new List<PadSlot> { S("a", 0, 0, 10, 10, false, 1), S("b", 20, 0, 10, 10, false, 5) };
            Assert.AreEqual(0, PadNavMath.Default(off), "доступних немає — перша недоступна (гравець бачить причину)");
            Assert.AreEqual(-1, PadNavMath.Default(new List<PadSlot>()), "порожній екран — нікуди");

            var show = new List<PadSlot> { S("skip", 0, 0, 10, 10, true, 5), S("gold", 40, 0, 10, 10, true, 2) };
            Assert.AreEqual(1, PadNavMath.Default(show), "афіша: «Вийти на ринг», а не «Зійти з вечора»");
        }

        [Test]
        public void Move_AlongTheRow_AndStopsAtTheEdge()
        {
            var w = Week();
            Assert.AreEqual(1, PadNavMath.Move(w, 0, PadDir.Right));
            Assert.AreEqual(4, PadNavMath.Move(w, 3, PadDir.Right));
            Assert.AreEqual(2, PadNavMath.Move(w, 3, PadDir.Left));
            Assert.AreEqual(-1, PadNavMath.Move(w, 0, PadDir.Left), "край екрана — по колу не ходимо");
            Assert.AreEqual(8, PadNavMath.Move(w, 0, PadDir.Down), "униз з будь-якої плитки — на золоту плиту (головна дія не загублена)");
            Assert.AreEqual(0, PadNavMath.Move(w, -1, PadDir.Right), "фокусу ще немає — стати на ціль за замовчуванням");
        }

        [Test]
        public void Move_UpReachesTheTabs_DownReachesTheGoldPlate()
        {
            var w = Week();
            Assert.AreEqual(6, PadNavMath.Move(w, 5, PadDir.Up), "з плитки під вкладкою — на вкладку «Розвиток»");
            Assert.AreEqual(6, PadNavMath.Move(w, 0, PadDir.Up), "з далекої плитки — теж на найближчу вкладку (не застрягти)");
            Assert.AreEqual(5, PadNavMath.Move(w, 6, PadDir.Down), "з вкладки — на плитку під нею");
            Assert.AreEqual(8, PadNavMath.Move(w, 5, PadDir.Down), "з останньої плитки — на золоту плиту");
            Assert.AreEqual(7, PadNavMath.Move(w, 6, PadDir.Right), "вкладки — одна за одною");
        }

        [Test]
        public void Move_TwoRows_PicksTheTileStraightAcross()
        {
            // 4:3: три плитки зверху, три знизу
            var s = new List<PadSlot>();
            for (int i = 0; i < 6; i++) s.Add(S("t" + i, 40f + (i % 3) * 190f, i < 3 ? 400f : 560f, 180f, 130f));
            Assert.AreEqual(4, PadNavMath.Move(s, 1, PadDir.Down), "під середньою — середня");
            Assert.AreEqual(2, PadNavMath.Move(s, 5, PadDir.Up), "над правою — права");
            Assert.AreEqual(-1, PadNavMath.Move(s, 2, PadDir.Right), "справа від правої верхньої нічого: нижній ряд не «праворуч»");
            Assert.AreEqual(4, PadNavMath.Move(s, 3, PadDir.Right), "у своєму ряду — сусід, не діагональ");
        }

        [Test]
        public void Move_SkipsTargetsWithoutLayout_AndFromTargetWithoutLayoutStays()
        {
            var s = new List<PadSlot> { S("a", 0, 0), S("b", float.NaN, 0), S("c", 400, 0) };
            Assert.AreEqual(2, PadNavMath.Move(s, 0, PadDir.Right), "ціль без розкладки (щойно перебудована) пропускається");
            Assert.AreEqual(-1, PadNavMath.Move(s, 1, PadDir.Right), "сам без розкладки — стоїмо до наступного кадру");
            var zero = new List<PadSlot> { S("a", 0, 0), S("hidden", 200, 0, 0, 0) };
            Assert.AreEqual(-1, PadNavMath.Move(zero, 0, PadDir.Right), "нульовий розмір — не ціль");
        }

        [Test]
        public void Move_DrawerList_GoesItemByItem()
        {
            var s = new List<PadSlot>();
            for (int i = 0; i < 8; i++) s.Add(S("row" + i, 900f, 200f + i * 44f, 420f, 40f));
            for (int i = 0; i < 7; i++) Assert.AreEqual(i + 1, PadNavMath.Move(s, i, PadDir.Down));
            Assert.AreEqual(-1, PadNavMath.Move(s, 7, PadDir.Down));
            Assert.AreEqual(2, PadNavMath.Move(s, 3, PadDir.Up));
        }

        [Test]
        public void Restore_ByKey_ElseDefault_AndKeysAreUnique()
        {
            var w = Week();
            Assert.AreEqual(7, PadNavMath.Restore(w, "tab:shop"), "після перебудови — та сама вкладка");
            Assert.AreEqual(0, PadNavMath.Restore(w, "tile:промо-нагорода"), "цілі вже немає — за замовчуванням");
            Assert.AreEqual(0, PadNavMath.Restore(w, null));

            var keys = PadNavMath.UniqueKeys(new[] { "btn:Купити", "btn:Купити", "btn:Далі", null, "btn:Купити" });
            CollectionAssert.AreEqual(new[] { "btn:Купити", "btn:Купити#2", "btn:Далі", "", "btn:Купити#3" }, keys);
        }

        [Test]
        public void ScrollToShow_BringsTheItemIntoTheDrawer()
        {
            // вікно шухляди 300 заввишки з 100; рядок 40
            Assert.AreEqual(0f, PadNavMath.ScrollToShow(0f, 100f, 300f, 150f, 40f), "видно — не крутити");
            Assert.AreEqual(48f, PadNavMath.ScrollToShow(0f, 100f, 300f, 400f, 40f), 0.01f, "нижче вікна — вниз до низу рядка з полем 8");
            Assert.AreEqual(108f, PadNavMath.ScrollToShow(120f, 100f, 300f, 96f, 40f), 0.01f, "вище вікна — вгору до верху рядка з полем");
            Assert.AreEqual(0f, PadNavMath.ScrollToShow(10f, 100f, 300f, 60f, 40f), "не менше нуля");
            Assert.AreEqual(5f, PadNavMath.ScrollToShow(5f, float.NaN, 300f, 60f, 40f), "без розкладки — як було");
        }
    }
}
