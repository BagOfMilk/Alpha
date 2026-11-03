using System.Collections.Generic;
using System.Linq;
using Game.Core.Ring;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>Валідатор графа промо (gdd §4.2): по тесту на кожен клас помилки.</summary>
    public class PromoValidatorTests
    {
        private static List<PromoError> Validate(FeudScript s, Dictionary<string, string> strings = null)
        {
            return PromoValidator.Validate(s, strings ?? RingTestData.Strings());
        }

        private static PromoError One(List<PromoError> errs, PromoErrorCode code)
        {
            var hit = errs.Where(e => e.Code == code).ToList();
            Assert.AreEqual(1, hit.Count, "очікувалась рівно одна помилка " + code + "; є: " + string.Join("; ", errs.Select(e => e.ToString())));
            return hit[0];
        }

        [Test]
        public void ValidGraph_HasNoErrors()
        {
            var errs = Validate(RingTestData.Script());
            Assert.IsEmpty(errs, string.Join("; ", errs.Select(e => e.ToString())));
        }

        [Test]
        public void MissingNext_IsReported_WithNodeId()
        {
            var s = RingTestData.Script();
            s.Promos[0].Find("n1").Next = "zzz";
            var e = One(Validate(s), PromoErrorCode.MissingNext);
            Assert.AreEqual("n1", e.NodeId);
            Assert.AreEqual("p1", e.PromoId);
        }

        [Test]
        public void MissingNext_InChoiceOptionAndCheckNext6()
        {
            var s = RingTestData.Script();
            s.Promos[0].Find("n2").Options[0].Next = "zzz";
            s.Promos[0].Find("n3").Next6 = "yyy";
            var errs = Validate(s).Where(x => x.Code == PromoErrorCode.MissingNext).ToList();
            CollectionAssert.AreEquivalent(new[] { "n2", "n3" }, errs.Select(x => x.NodeId).ToArray());
        }

        [Test]
        public void UnreachableNode_IsReported()
        {
            var s = RingTestData.Script();
            s.Promos[0].Nodes.Add(new PromoNode { Id = "orphan", Type = PromoNodeType.Line, Speaker = "rival", TextKey = "k.n1", Next = "end" });
            var e = One(Validate(s), PromoErrorCode.Unreachable);
            Assert.AreEqual("orphan", e.NodeId);
        }

        [Test]
        public void NoPathToEnd_LoopWithoutExit_IsReported()
        {
            var s = RingTestData.Script();
            var p = s.Promos[0];
            p.Nodes.Add(new PromoNode { Id = "l1", Type = PromoNodeType.Line, Speaker = "rival", TextKey = "k.n1", Next = "l2" });
            p.Nodes.Add(new PromoNode { Id = "l2", Type = PromoNodeType.Line, Speaker = "rival", TextKey = "k.n1", Next = "l1" });
            p.Find("n4").Options[1].Next = "l1";
            var errs = Validate(s).Where(x => x.Code == PromoErrorCode.NoPathToEnd).ToList();
            CollectionAssert.AreEquivalent(new[] { "l1", "l2" }, errs.Select(x => x.NodeId).ToArray());
        }

        [Test]
        public void Choice_WithoutAnyOptionFree_IsReported()
        {
            var s = RingTestData.Script();
            var flag = new FeudCondition { Kind = ConditionKind.Flag, Key = "x" };
            var n2 = s.Promos[0].Find("n2");
            n2.Options[0].Requires.Add(flag);
            n2.Options[2].Requires.Add(flag);
            var e = One(Validate(s), PromoErrorCode.NoFreeOption);
            Assert.AreEqual("n2", e.NodeId);
        }

        [Test]
        public void Option_WithoutEffect_IsReported()
        {
            var s = RingTestData.Script();
            s.Promos[1].Find("m1").Options[1].Effects.Clear();
            var e = One(Validate(s), PromoErrorCode.OptionWithoutEffect);
            Assert.AreEqual("m1", e.NodeId);
            StringAssert.Contains("'b'", e.Message);
        }

        [Test]
        public void Check_PickMoreThanRewards_IsReported()
        {
            var s = RingTestData.Script();
            s.Promos[0].Find("n3").Pick10 = 4;
            var e = One(Validate(s), PromoErrorCode.PickTooMany);
            Assert.AreEqual("n3", e.NodeId);
        }

        [Test]
        public void MissingTextKeys_AreReported_ForLinesOptionsRewardsTitlesNamesAndSpeakers()
        {
            foreach (var key in new[] { "k.n1", "k.respect", "r.mom", "p1.title", "p2.place", "w.rival.name", "speaker.rival", "feud.title" })
            {
                var strings = RingTestData.Strings();
                strings.Remove(key);
                var errs = Validate(RingTestData.Script(), strings);
                Assert.IsTrue(errs.Any(e => e.Code == PromoErrorCode.MissingText && e.Message.Contains("'" + key + "'")), "не піймано відсутній ключ " + key);
            }
        }

        [Test]
        public void EmptyStringValue_CountsAsMissing()
        {
            var strings = RingTestData.Strings();
            strings["k.deal"] = "";
            Assert.IsTrue(Validate(RingTestData.Script(), strings).Any(e => e.Code == PromoErrorCode.MissingText));
        }

        [Test]
        public void MissingStart_And_DuplicateNodeId_And_NoEnd()
        {
            var s = RingTestData.Script();
            s.Promos[1].Start = "nowhere";
            One(Validate(s), PromoErrorCode.MissingStart);

            s = RingTestData.Script();
            s.Promos[1].Nodes.Add(new PromoNode { Id = "end", Type = PromoNodeType.End });
            One(Validate(s), PromoErrorCode.DuplicateNodeId);

            s = RingTestData.Script();
            s.Promos[1].Nodes.RemoveAll(n => n.Type == PromoNodeType.End);
            var errs = Validate(s);
            One(errs, PromoErrorCode.NoEnd);
            Assert.IsTrue(errs.Any(e => e.Code == PromoErrorCode.MissingNext), "перехід на видалений end");
        }

        [Test]
        public void EmptyChoice_IsReported()
        {
            var s = RingTestData.Script();
            s.Promos[1].Find("m1").Options.Clear();
            One(Validate(s), PromoErrorCode.EmptyChoice);
        }
    }
}
