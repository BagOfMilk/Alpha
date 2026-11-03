using System.Collections.Generic;
using Game.Core.Ring;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>Мінімальний JSON-розбирач ядра «Рингу»: типи значень, екранування, помилки з позицією.</summary>
    public class MiniJsonTests
    {
        [Test]
        public void Parses_ObjectsArraysAndPrimitives()
        {
            var o = MiniJson.ParseObject("{\"a\": 1, \"b\": [true, false, null, \"x\"], \"c\": {\"d\": -2.5e1}}");
            Assert.AreEqual(1.0, (double)o["a"]);
            var b = (List<object>)o["b"];
            Assert.AreEqual(true, b[0]);
            Assert.AreEqual(false, b[1]);
            Assert.IsNull(b[2]);
            Assert.AreEqual("x", b[3]);
            Assert.AreEqual(-25.0, (double)((Dictionary<string, object>)o["c"])["d"]);
        }

        [Test]
        public void Parses_StringEscapes()
        {
            var o = MiniJson.ParseObject("{\"s\": \"q\\\"b\\\\n\\n\\t\\u0416\\/\"}");
            Assert.AreEqual("q\"b\\n\n\t\u0416/", (string)o["s"]);
        }

        [Test]
        public void Parses_EmptyContainersAndWhitespace()
        {
            var o = MiniJson.ParseObject(" \n{ \"a\" : [ ] , \"b\":{ } }\r\n");
            Assert.AreEqual(0, ((List<object>)o["a"]).Count);
            Assert.AreEqual(0, ((Dictionary<string, object>)o["b"]).Count);
        }

        [Test]
        public void Parses_UkrainianTextAsIs()
        {
            var o = MiniJson.ParseObject("{\"k\": \"Ворожнеча — це їжа\"}");
            Assert.AreEqual("Ворожнеча — це їжа", (string)o["k"]);
        }

        [TestCase("{\"a\": }", 6)]
        [TestCase("{\"a\" 1}", 5)]
        [TestCase("[1, 2", 5)]
        [TestCase("{\"a\": 1} x", 9)]
        [TestCase("{\"a\": tru}", 6)]
        [TestCase("{\"a\": \"abc", 10)]
        public void Errors_CarryPosition(string json, int expectedPos)
        {
            var ex = Assert.Throws<MiniJsonException>(() => MiniJson.Parse(json));
            Assert.AreEqual(expectedPos, ex.Position);
        }

        [Test]
        public void Errors_DuplicateKey_BadEscape_TopLevelNotObject()
        {
            Assert.Throws<MiniJsonException>(() => MiniJson.Parse("{\"a\": 1, \"a\": 2}"));
            Assert.Throws<MiniJsonException>(() => MiniJson.Parse("\"a\\qb\""));
            Assert.Throws<MiniJsonException>(() => MiniJson.ParseObject("[1]"));
            Assert.Throws<MiniJsonException>(() => MiniJson.Parse("\"\\u12\""));
        }
    }
}
