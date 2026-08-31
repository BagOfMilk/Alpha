using Game.Core.Balance;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Забор от той самой ошибки, из-за которой Командир на разведпосту обгонял
    /// профильного Разведчика: боевой стат в шкале 0–100 попадал в формулу
    /// выработки рядом со склонностями 0–7. Теперь шкалы объявлены и проверяются.
    /// </summary>
    public class StatScaleTests
    {
        private static BalanceConfig Cfg() => new BalanceConfig();

        [Test]
        public void DeclaredScales_AreOneToTenAndZeroToTen()
        {
            var cfg = Cfg();
            Assert.AreEqual(1, cfg.MinAttribute);
            Assert.AreEqual(10, cfg.MaxAttribute);
            Assert.AreEqual(10, cfg.MaxSkillLevel);
        }

        [Test]
        public void AttributeOutsideScale_IsReported()
        {
            var cfg = Cfg();
            var attrs = new AttributeSet(strength: 45, agility: 4, wits: 4, will: 4); // старая шкала 0–100
            var problems = StatScales.Violations(attrs, new SkillSet(), cfg, "командир");

            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains("Сила", problems[0]);
            StringAssert.Contains("командир", problems[0]);
        }

        [Test]
        public void SkillOutsideScale_IsReported()
        {
            var cfg = Cfg();
            var skills = new SkillSet();
            skills[SkillType.Ranged] = 65; // старая шкала точности
            var problems = StatScales.Violations(new AttributeSet(4, 4, 4, 4), skills, cfg);

            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains("Стрелковое", problems[0]);
        }

        [Test]
        public void ContentInsideScales_HasNoViolations()
        {
            var cfg = Cfg();
            var skills = new SkillSet();
            skills[SkillType.Medicine] = 10;
            skills[SkillType.Trade] = 0;

            var problems = StatScales.Violations(new AttributeSet(1, 10, 5, 5), skills, cfg);
            CollectionAssert.IsEmpty(problems);
        }

        [Test]
        public void Clamp_PullsValuesIntoScale()
        {
            var cfg = Cfg();
            Assert.AreEqual(10, StatScales.ClampAttribute(45, cfg));
            Assert.AreEqual(1, StatScales.ClampAttribute(0, cfg));
            Assert.AreEqual(10, StatScales.ClampSkill(65, cfg));
            Assert.AreEqual(0, StatScales.ClampSkill(-3, cfg));
        }
    }
}
