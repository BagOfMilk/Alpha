using Game.Gameplay;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Бій v2 (docs/COMBAT_V2.md §7.3): розбір <c>args["path"]</c> журналу
    /// (<c>combat.log.move</c>) у список тайлів — чиста функція, використовує
    /// презентер (<see cref="BattleArenaController"/>) для покрокового
    /// відтворення руху (такти, §6).
    /// </summary>
    public class BattleTactParserTests
    {
        [Test]
        public void ParsePath_Null_ReturnsEmpty()
        {
            Assert.AreEqual(0, BattleTactParser.ParsePath(null).Count);
        }

        [Test]
        public void ParsePath_Empty_ReturnsEmpty()
        {
            Assert.AreEqual(0, BattleTactParser.ParsePath(string.Empty).Count);
        }

        [Test]
        public void ParsePath_SingleTile()
        {
            var tiles = BattleTactParser.ParsePath("3,4");
            Assert.AreEqual(1, tiles.Count);
            Assert.AreEqual(3, tiles[0].X);
            Assert.AreEqual(4, tiles[0].Y);
        }

        [Test]
        public void ParsePath_MultipleTiles_KeepsOrder()
        {
            var tiles = BattleTactParser.ParsePath("1,1;2,1;2,2");
            Assert.AreEqual(3, tiles.Count);
            Assert.AreEqual(1, tiles[0].X); Assert.AreEqual(1, tiles[0].Y);
            Assert.AreEqual(2, tiles[1].X); Assert.AreEqual(1, tiles[1].Y);
            Assert.AreEqual(2, tiles[2].X); Assert.AreEqual(2, tiles[2].Y);
        }

        [Test]
        public void ParsePath_MalformedToken_IsSkipped_NotThrown()
        {
            var tiles = BattleTactParser.ParsePath("1,1;garbage;3,3");
            Assert.AreEqual(2, tiles.Count);
            Assert.AreEqual(1, tiles[0].X);
            Assert.AreEqual(3, tiles[1].X);
        }

        [Test]
        public void ParsePath_TrailingSemicolon_DoesNotAddEmptyTile()
        {
            var tiles = BattleTactParser.ParsePath("1,1;2,2;");
            Assert.AreEqual(2, tiles.Count);
        }

        [Test]
        public void ParsePath_NegativeCoordinates_AreParsed()
        {
            var tiles = BattleTactParser.ParsePath("-1,2");
            Assert.AreEqual(1, tiles.Count);
            Assert.AreEqual(-1, tiles[0].X);
            Assert.AreEqual(2, tiles[0].Y);
        }

        [Test]
        public void FormatPath_RoundTrips_WithParsePath()
        {
            var tiles = BattleTactParser.ParsePath("1,1;2,1;2,2");
            string formatted = BattleTactParser.FormatPath(tiles);
            Assert.AreEqual("1,1;2,1;2,2", formatted);
        }

        [Test]
        public void FormatPath_Empty_ReturnsEmptyString()
        {
            Assert.AreEqual(string.Empty, BattleTactParser.FormatPath(BattleTactParser.ParsePath(null)));
        }
    }
}
