using Game.Core.Story;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// StoryFlags (R3, Foundation/A1) — чистий контейнер: Get/Set/CaptureState/
    /// RestoreState і нічого більше. Подій сам не емітить — це робота
    /// GameSession, коли він з'явиться (див. коментар класу).
    /// </summary>
    public class StoryFlagsTests
    {
        [Test]
        public void Get_UnsetFlag_IsFalse()
        {
            var flags = new StoryFlags();
            Assert.IsFalse(flags.Get("tugar_offer_seen"));
        }

        [Test]
        public void Set_ThenGet_ReturnsTrue()
        {
            var flags = new StoryFlags();
            flags.Set("tugar_offer_seen");
            Assert.IsTrue(flags.Get("tugar_offer_seen"));
        }

        [Test]
        public void Set_False_ClearsAnEarlierTrue()
        {
            var flags = new StoryFlags();
            flags.Set("defector_seeded", true);
            flags.Set("defector_seeded", false);
            Assert.IsFalse(flags.Get("defector_seeded"));
        }

        [Test]
        public void Get_UnknownOrEmptyId_IsFalseNotAnException()
        {
            var flags = new StoryFlags();
            Assert.IsFalse(flags.Get(null));
            Assert.IsFalse(flags.Get(string.Empty));
            Assert.IsFalse(flags.Get("no_such_flag"));
        }

        [Test]
        public void CaptureState_Empty_IsEmptyString()
        {
            var flags = new StoryFlags();
            Assert.AreEqual(string.Empty, flags.CaptureState());
        }

        [Test]
        public void CaptureState_OnlyListsFlagsSetToTrue()
        {
            var flags = new StoryFlags();
            flags.Set("tugar_offer_seen");
            flags.Set("defector_seeded", true);
            flags.Set("crisis_test_mitigated", false); // виставлений, але в false — не повинен потрапити в блоб

            string blob = flags.CaptureState();

            StringAssert.Contains("tugar_offer_seen", blob);
            StringAssert.Contains("defector_seeded", blob);
            StringAssert.DoesNotContain("crisis_test_mitigated", blob);
        }

        [Test]
        public void RoundTrip_RestoresExactlyTheSetOfTrueFlags()
        {
            var flags = new StoryFlags();
            flags.Set("tugar_offer_seen");
            flags.Set("hafiya_quest_active");
            flags.Set("defector_seeded", false);

            var restored = new StoryFlags();
            restored.RestoreState(flags.CaptureState());

            Assert.IsTrue(restored.Get("tugar_offer_seen"));
            Assert.IsTrue(restored.Get("hafiya_quest_active"));
            Assert.IsFalse(restored.Get("defector_seeded"));
            Assert.AreEqual(flags.CaptureState(), restored.CaptureState(),
                "Повторный CaptureState после восстановления обязан быть тем же самым");
        }

        [Test]
        public void RestoreState_EmptyOrNullBlob_ClearsFlags()
        {
            var flags = new StoryFlags();
            flags.Set("tugar_offer_seen");

            flags.RestoreState(null);
            Assert.IsFalse(flags.Get("tugar_offer_seen"));

            flags.Set("tugar_offer_seen");
            flags.RestoreState(string.Empty);
            Assert.IsFalse(flags.Get("tugar_offer_seen"));
        }
    }
}
