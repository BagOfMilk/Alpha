using System.Collections.Generic;
using System.Linq;
using Game.Core.Characters;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Квота кастингу (Поправка №17.3): ≈20 % українських від УСІХ іменних («УСІХ», власник
    /// 01.10.2026) і жодних російських першоджерел (№20: «НІЯКИХ РОСІЯН», скасовує «лише вороги»
    /// з №12.9). Іменних — 100 (№20: каст відкриття + світ попаданців). Правила — чисті функції
    /// <see cref="CastingRules"/>, тому кожен охоронець перевіряється і на справжньому каталозі,
    /// і на навмисно зіпсованих копіях (мутація без правки коду).
    /// </summary>
    public class CastingQuotaTests
    {
        private static CharacterCard Card(string id, SourceCulture culture, bool enemy = false, SourceTier tier = SourceTier.Literary) =>
            new CharacterCard(id, id, tier, "тестове першоджерело") { Culture = culture, IsEnemy = enemy };

        // ---------- справжній каталог ----------

        [Test]
        public void RealCast_HasNoViolations()
        {
            var problems = CastingRules.Violations(CastingRules.AllNamed());
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void RealCast_UkrainianShare_NeverGrows_Ratchet()
        {
            // «Храповик»: з 07.10.2026 (№20) українських 20 зі 100 — рівно ≈20 %. Нову українську
            // картку можна додати лише разом із чотирма неукраїнськими.
            var cast = CastingRules.AllNamed();
            double share = CastingRules.UkrainianShare(cast);
            Assert.LessOrEqual(share, 0.20 + 1e-9,
                "частка українських зросла (" + share.ToString("P0") + "): додайте неукраїнських іменних або приберіть українську картку");
        }

        [Test]
        public void RealCast_Counts_AreWhatTheAmendmentSays()
        {
            var all = CastingRules.AllNamed();
            Assert.AreEqual(100, all.Count, "Поправка №20: «Щоб усього було 100 персонажів»");
            Assert.AreEqual(20, all.Count(c => c.Culture == SourceCulture.Ukrainian), "«20 з них українці з різних епох»");
            Assert.AreEqual(0, all.Count(c => c.Culture == SourceCulture.Russian), "«НІЯКИХ РОСІЯН»");
            Assert.AreEqual(all.Count, all.Select(c => c.Id).Distinct().Count(), "id іменних унікальні");
            foreach (var c in all.Where(c => c.Tier != SourceTier.Original))
                Assert.IsFalse(string.IsNullOrWhiteSpace(c.Source), c.Id + ": запозичений без першоджерела");

            var cast = OpeningCast.All();
            Assert.AreEqual(9, cast.Count);
            Assert.AreEqual(6, cast.Count(c => c.Culture == SourceCulture.Ukrainian), "Максим, Мирослава, Тугар, Захар, Дід Овсій, Гафія");
            Assert.AreEqual(2, cast.Count(c => c.Culture == SourceCulture.Other), "Гобан-Сайр (ірландська), Синдбад (арабська)");
            Assert.AreEqual(1, cast.Count(c => c.Culture == SourceCulture.Unspecified), "командир орди — першоджерело ще не вибрано");
            Assert.AreEqual(0, cast.Count(c => c.Culture == SourceCulture.Russian), "російських першоджерел у касті немає");
        }

        [Test]
        public void RealCast_EnemiesAreNeverCompanionCandidates()
        {
            var candidates = OpeningCast.CompanionCandidates().Select(c => c.Id).ToList();
            foreach (var enemy in OpeningCast.All().Where(c => c.IsEnemy))
                CollectionAssert.DoesNotContain(candidates, enemy.Id, enemy.Id + ": ворог не напарник");
            Assert.IsTrue(OpeningCast.TuharVovk().IsEnemy);
            Assert.IsTrue(OpeningCast.HordeCommander().IsEnemy);
        }

        // ---------- зіпсовані копії: правила мають зуби ----------

        [Test]
        public void Rule_BorrowedCardWithoutCulture_IsAViolation()
        {
            var cards = new List<CharacterCard> { Card("a", SourceCulture.Unspecified) };
            Assert.IsNotEmpty(CastingRules.Violations(cards));
        }

        [Test]
        public void Rule_Russian_IsAViolation_EvenAsEnemy()
        {
            Assert.IsNotEmpty(CastingRules.Violations(new List<CharacterCard> { Card("ru", SourceCulture.Russian, enemy: false) }),
                "російське першоджерело напарником чи жителем бути не може");
            Assert.IsNotEmpty(CastingRules.Violations(new List<CharacterCard> { Card("ru", SourceCulture.Russian, enemy: true) }),
                "Поправка №20: «НІЯКИХ РОСІЯН» — і ворогом теж");
        }

        [Test]
        public void Rule_QuotaBand_SwitchesOnAtTwentyNamed()
        {
            // 19 іменних, усі українські: вилка ще не діє (частка 100 %, але менше 20 карток).
            var nineteen = Enumerable.Range(0, 19).Select(i => Card("u" + i, SourceCulture.Ukrainian)).ToList();
            Assert.IsEmpty(CastingRules.Violations(nineteen));

            // 20 іменних, 10 українських (50 %): поза вилкою 15–25 %.
            var half = Enumerable.Range(0, 10).Select(i => Card("u" + i, SourceCulture.Ukrainian))
                .Concat(Enumerable.Range(0, 10).Select(i => Card("o" + i, SourceCulture.Other))).ToList();
            Assert.IsNotEmpty(CastingRules.Violations(half));

            // 20 іменних, 4 українських (20 %): у вилці.
            var fifth = Enumerable.Range(0, 4).Select(i => Card("u" + i, SourceCulture.Ukrainian))
                .Concat(Enumerable.Range(0, 16).Select(i => Card("o" + i, SourceCulture.Other))).ToList();
            Assert.IsEmpty(CastingRules.Violations(fifth));

            // 20 іменних, 1 українська (5 %): нижче вилки.
            var scarce = Enumerable.Range(0, 1).Select(i => Card("u" + i, SourceCulture.Ukrainian))
                .Concat(Enumerable.Range(0, 19).Select(i => Card("o" + i, SourceCulture.Other))).ToList();
            Assert.IsNotEmpty(CastingRules.Violations(scarce));
        }

        [Test]
        public void NonUkrainianNeeded_MatchesTheAmendmentArithmetic()
        {
            // 6 українських і 3 неукраїнських (з командиром орди): 4×6 − 3 = 21 (Поправка №17.3: ≈ +21–22).
            Assert.AreEqual(21, CastingRules.NonUkrainianNeeded(6, 3));
            Assert.AreEqual(0, CastingRules.NonUkrainianNeeded(0, 5));
        }
    }
}
