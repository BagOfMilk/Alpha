using System;
using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Companions;
using Game.Core.Economy;
using Game.Core.Factions;
using Game.Core.Health;
using Game.Core.Items;
using Game.Core.Quests;
using Game.Core.Stats;
using Game.Core.Threats;

namespace Game.Core.Saves
{
    /// <summary>
    /// Снимок и восстановление кампании (US-16.1). Чистый C# (без Unity) — сериализация
    /// SaveData ↔ файл живёт в Gameplay-обёртке. Capture снимает рантайм-состояние,
    /// Restore собирает свежую кампанию из снимка (контент — по id через ContentCatalog).
    /// </summary>
    public static class SaveSystem
    {
        // ---- Снимок ----
        public static SaveData Capture(Campaign campaign)
        {
            var b = campaign.Base;
            var data = new SaveData
            {
                day = b.CurrentDay,
                cityTier = b.CityTier,
                population = (float)b.Population,
                gold = b.Resources.Get(ResourceType.Gold),
                buildingMaterial = b.Resources.Get(ResourceType.BuildingMaterial),
                craftingMaterial = b.Resources.Get(ResourceType.CraftingMaterial),
                tension = (float)(b.ThreatsSystem != null ? b.ThreatsSystem.Tension.Value : 0),
                readiness = (float)(b.ThreatsSystem != null ? b.ThreatsSystem.Readiness.Value : 0),
                ironman = campaign.Ironman,
                inExpedition = campaign.InExpedition,
                campaignOutcome = (int)campaign.Outcome,
                campaignSeed = campaign.Seed,
                finaleReadyDay = campaign.FinaleReadyDay,
                reputation = (float)campaign.Factions.Reputation,
                influence = campaign.Factions.Influence
            };

            // Состояние потока угроз: загрузка продолжит его, а не перезапустит.
            var threatsRng = b.ThreatsSystem != null ? b.ThreatsSystem.Rng as SeededRng : null;
            if (threatsRng != null) data.threatsRngState = threatsRng.State.ToString();
            if (b.ThreatsSystem != null)
                foreach (var t in b.ThreatsSystem.FiredSpikes) data.firedSpikeThresholds.Add(t);

            foreach (var c in b.Roster.All) data.companions.Add(CaptureCompanion(c));
            foreach (var item in b.Inventory.Items) data.inventory.Add(CaptureItem(item));
            foreach (var slot in b.Slots)
            {
                if (slot.Unlocked) data.unlockedSlots.Add(slot.Id);
                if (slot.IsOccupied)
                    data.assignments.Add(new SlotDto { slotId = slot.Id, companionId = slot.AssignedCompanionId });
            }
            foreach (var standing in campaign.Factions.Standings)
                data.factions.Add(new FactionDto { id = standing.Faction.Id, value = (float)standing.Value });
            foreach (var flag in campaign.Flags) data.flags.Add(flag);
            foreach (var ach in campaign.Achievements) data.achievements.Add(ach);

            // Журнал квестов (v4): только id — контент восстановится из пула.
            foreach (var q in campaign.Quests.Available) data.questsAvailable.Add(q.Id);
            foreach (var q in campaign.Quests.Active) data.questsActive.Add(q.Id);
            foreach (var q in campaign.Quests.Completed) data.questsCompleted.Add(q.Id);

            // Позиция идущего прогона (v6): без неё загрузка начинала квест заново,
            // а уже применённые последствия выбора оставались в сейве — их можно
            // было фармить перезагрузкой (репутация фракций, Напряжение, лояльность).
            if (campaign.ActiveQuest != null)
            {
                data.activeQuestId = campaign.ActiveQuest.Def.Id;
                data.activeQuestIndex = campaign.ActiveQuest.CurrentIndex;
                data.activeQuestState = (int)campaign.ActiveQuest.State;
                data.activeArcId = campaign.ActiveArcId ?? "";
            }

            foreach (var section in b.BuiltSections) data.builtSections.Add((int)section);
            foreach (var con in b.ConstructionQueue)
                data.constructions.Add(new ConstructionDto
                {
                    id = con.Id,
                    displayName = con.DisplayName,
                    section = (int)con.Section,
                    totalDays = (float)con.TotalDays,
                    remainingDays = (float)con.RemainingDays,
                    unlocksSlotId = con.UnlocksSlotId
                });

            foreach (var run in campaign.Arcs)
                data.arcs.Add(new ArcDto { arcId = run.Arc.Id, state = (int)run.State, chapterIndex = run.ChapterIndex });

            foreach (var rec in campaign.Antagonists)
            {
                var dto = new AntagonistDto { companionId = rec.CompanionId, level = rec.Level };
                foreach (var item in rec.CapturedGear) dto.gear.Add(CaptureItem(item));
                data.antagonists.Add(dto);
            }

            if (campaign.Council != null)
            {
                data.councilAttached = true;
                foreach (var kv in campaign.Council.Cooldowns)
                    data.councilCooldowns.Add(new CouncilCooldownDto { actionId = kv.Key, days = kv.Value });
                data.investmentGoldPerDay = campaign.Council.InvestmentGoldPerDay;
                data.investmentDaysRemaining = campaign.Council.InvestmentDaysRemaining;
                var buff = campaign.Council.PendingExpeditionBuff;
                if (buff != null)
                {
                    data.hasExpeditionBuff = true;
                    data.buffAccuracyBonus = buff.AccuracyBonus;
                    data.buffBonusLootGold = buff.BonusLootGold;
                }
            }

            return data;
        }

        private static CompanionDto CaptureCompanion(Companion c)
        {
            var dto = new CompanionDto
            {
                id = c.Id,
                displayName = c.DisplayName,
                isProtagonist = c.IsProtagonist,
                strength = c.GetAttribute(AttributeType.Strength),
                agility = c.GetAttribute(AttributeType.Agility),
                wits = c.GetAttribute(AttributeType.Wits),
                will = c.GetAttribute(AttributeType.Will),
                level = c.Level,
                xp = c.Xp,
                unspentSkillPoints = c.UnspentSkillPoints,
                status = (int)c.Status,
                loyalty = c.Loyalty,
                injuryTier = (int)c.CurrentInjury,
                recoveryDays = (float)c.RecoveryDaysRemaining
            };

            foreach (SkillType skill in Enum.GetValues(typeof(SkillType)))
            {
                if (skill == SkillType.None) continue;
                int lvl = c.GetSkill(skill);
                if (lvl != 0) dto.skills.Add(new SkillDto { skill = (int)skill, level = lvl });
            }
            foreach (var t in c.Traits.Traits) dto.traitIds.Add(t.Id);
            foreach (var s in c.Scars.Scars) dto.scarIds.Add(s.Id);
            foreach (EquipSlot slot in Enum.GetValues(typeof(EquipSlot)))
            {
                var item = c.Equipment.Get(slot);
                if (item != null) dto.equipment.Add(CaptureItem(item));
            }
            return dto;
        }

        private static ItemDto CaptureItem(ItemInstance item)
        {
            var dto = new ItemDto { defId = item.Definition.Id, rarity = (int)item.Rarity };
            // Только БАЗОВЫЕ роллы (StatMods) — эффект именного восстановится из Definition.
            foreach (var m in item.StatMods)
                dto.mods.Add(new ModDto { stat = (int)m.Stat, value = (float)m.Value, mode = (int)m.Mode });
            return dto;
        }

        // ---- Восстановление ----
        public static Campaign Restore(SaveData data, BalanceConfig cfg, ContentCatalog catalog)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            cfg = cfg ?? new BalanceConfig();
            catalog = catalog ?? ContentCatalog.Default();

            var roster = new Roster();
            foreach (var cd in data.companions) roster.Add(RestoreCompanion(cd, cfg, catalog));

            // Вылазка НЕ сериализуется (Expedition/CombatState вне сейва): прерванную
            // считаем отменённой — «отряд вернулся домой». Иначе восстановленный
            // InSquad лочит напарников навсегда (софтлок: IsAvailableForDuty=false).
            foreach (var c in roster.All)
                if (c.Status == CompanionStatus.InSquad) c.Status = CompanionStatus.InCamp;

            var baseState = new BaseState(roster, new ResourceLedger(), cfg);
            foreach (var slot in DefaultContent.AllSlots()) baseState.AddSlot(slot);
            baseState.RestoreTime(data.day, data.population, data.cityTier);
            baseState.Resources.Add(ResourceType.Gold, data.gold);
            baseState.Resources.Add(ResourceType.BuildingMaterial, data.buildingMaterial);
            baseState.Resources.Add(ResourceType.CraftingMaterial, data.craftingMaterial);

            // Город (v2): построенные здания, открытые позиции (до назначений!), идущие стройки.
            foreach (var s in data.builtSections) baseState.MarkBuilt((BaseSectionType)s);
            if (data.version < 2)
            {
                // Миграция v1: ядро-здания считались стоящими всегда.
                baseState.MarkBuilt(BaseSectionType.Council);
                baseState.MarkBuilt(BaseSectionType.Infirmary);
                baseState.MarkBuilt(BaseSectionType.Workshop);
                baseState.MarkBuilt(BaseSectionType.Storehouse);

                // В v1 назначить можно было только на ОТКРЫТУЮ позицию — выводим
                // разблокировки/застройку из назначений (иначе market_stall заперся бы).
                foreach (var sd in data.assignments)
                {
                    var s = baseState.GetSlot(sd.slotId);
                    if (s == null) continue;
                    s.Unlocked = true;
                    baseState.MarkBuilt(s.Definition.Section);
                }
            }
            foreach (var slotId in data.unlockedSlots)
            {
                var slot = baseState.GetSlot(slotId);
                if (slot != null) slot.Unlocked = true;
            }
            foreach (var cd in data.constructions)
                baseState.RestoreConstruction(new Construction(cd.id, cd.displayName,
                    (BaseSectionType)cd.section, cd.totalDays, cd.unlocksSlotId)
                { RemainingDays = cd.remainingDays });

            // Поток угроз: восстановленное состояние продолжает последовательность
            // (анти-save-scum); миграция v1/v2 без состояния — пересев от сида+дня.
            var threatsRng = new SeededRng(Campaign.DeriveSeed(data.campaignSeed, 1) + data.day);
            if (!string.IsNullOrEmpty(data.threatsRngState) && ulong.TryParse(data.threatsRngState, out var rngState))
                threatsRng.RestoreState(rngState);
            var threats = new ThreatSystem(cfg, threatsRng,
                DefaultContent.IncidentPool(), DefaultContent.TensionSpikes(), startingTension: data.tension);
            threats.Readiness.Add(data.readiness);
            var spent = new List<double>(data.firedSpikeThresholds);
            if (data.version < 3)
            {
                // Миграция v1/v2: fired-набор не сохранялся, но старый код стрелял
                // всплеск сразу при достижении порога — всё ≤ tension уже потрачено
                // (иначе первый тик после загрузки повторил бы одноразовый кризис).
                foreach (var spike in DefaultContent.TensionSpikes())
                    if (spike.Threshold <= data.tension) spent.Add(spike.Threshold);
            }
            threats.RestoreFiredSpikes(spent);
            baseState.AttachThreats(threats);

            foreach (var it in data.inventory)
            {
                var inst = RestoreItem(it, catalog);
                if (inst != null) baseState.Inventory.Add(inst);
            }

            var factions = DefaultFactions.NewRegistry();
            foreach (var fd in data.factions) factions.Adjust(fd.id, fd.value); // из 0 → сохранённое
            if (data.reputation != 0) factions.AdjustReputation(data.reputation);
            if (data.influence != 0) factions.AddInfluence(data.influence);

            foreach (var sd in data.assignments) baseState.TryAssign(sd.companionId, sd.slotId);

            // Инвариант «на посту ⇔ есть пост»: если назначение не восстановилось
            // (потерянный/запертый слот) — напарник возвращается в лагерь.
            foreach (var c in roster.All)
                if ((c.Status == CompanionStatus.OnDuty || c.Status == CompanionStatus.OnCouncil) && !c.IsAssigned)
                    c.Status = CompanionStatus.InCamp;

            var campaign = new Campaign(cfg, baseState, factions)
            {
                Ironman = data.ironman,
                // Прерванная вылазка отменена при загрузке (см. нормализацию InSquad выше);
                // data.inExpedition остаётся в сейве под будущую полную сериализацию вылазки.
                InExpedition = false,
                Outcome = (CampaignOutcome)data.campaignOutcome,
                Seed = data.campaignSeed,
                FinaleReadyDay = data.finaleReadyDay
            };
            foreach (var f in data.flags) campaign.Flags.Add(f);
            foreach (var a in data.achievements) campaign.Achievements.Add(a);

            // Идущий прогон (v6) восстанавливается ПОЗИЦИЕЙ: тот же этап, то же
            // состояние. До v6 квест начинался заново, а уже применённые последствия
            // (репутация/Напряжение/лояльность) оставались — перезагрузкой их можно
            // было накручивать сколько угодно.
            RestoreActiveQuest(data, campaign, catalog);

            // Журнал квестов (v4): статусы по id на свежий пул. Активным остаётся
            // тот, чей прогон восстановлен; прочие «активные» (сейвы v1–v5 или
            // потерянный контент) демотируются в «доступные» — перепройти с начала
            // честнее полусостояния; гейты (BlockedByFlag) при этом перепроверяются,
            // и квест с уже стоящим блок-флагом на доску не вернётся.
            // v1–v3 (журнала не было): списки пусты, доска соберётся через CollectFrom.
            var questsAvailable = new List<string>(data.questsAvailable);
            var questsActive = new List<string>();
            bool boardRunRestored = campaign.ActiveQuest != null && campaign.ActiveArcId == null;
            foreach (var id in data.questsActive)
            {
                if (boardRunRestored && id == campaign.ActiveQuest.Def.Id) questsActive.Add(id);
                else questsAvailable.Add(id);
            }
            campaign.Quests.Restore(DefaultQuests.FullPool(),
                questsAvailable, questsActive, data.questsCompleted,
                factions, campaign.Flags, roster.All);

            // Совет (v2): пере-подключаем с пережившими сейв КД/инвестицией/бафом.
            if (data.councilAttached)
            {
                var council = Council.DefaultCouncil.NewCouncil(factions, baseState.Resources, threats, baseState);
                var cooldowns = new List<KeyValuePair<string, int>>();
                foreach (var cd in data.councilCooldowns)
                    cooldowns.Add(new KeyValuePair<string, int>(cd.actionId, cd.days));
                council.RestoreState(cooldowns,
                    data.investmentGoldPerDay, data.investmentDaysRemaining,
                    data.hasExpeditionBuff
                        ? new Council.ExpeditionBuff
                        { AccuracyBonus = data.buffAccuracyBonus, BonusLootGold = data.buffBonusLootGold }
                        : null);
                campaign.AttachCouncil(council);
            }

            // Арки напарников (v2): контент по id, прогресс — из сейва.
            foreach (var ad in data.arcs)
            {
                var arc = catalog.GetArc(ad.arcId);
                if (arc == null) continue;
                var run = new CompanionArcRun(arc, campaign.Flags);
                var state = (ArcState)ad.state;
                // Идущая глава остаётся InProgress ТОЛЬКО если её прогон реально
                // восстановлен (v6). Иначе ДЕМОТИРУЕМ: InProgress пережил бы сейв,
                // Refresh на нём выходит сразу, а двигать арку нечем — контент мёртв
                // навсегда.
                bool chapterRunRestored = campaign.ActiveArcId == ad.arcId;
                if (state == ArcState.InProgress && !chapterRunRestored)
                {
                    run.RestoreState(ArcState.Locked, ad.chapterIndex);
                    run.Refresh(roster.Get(arc.CompanionId)); // гейт перепроверится → снова на доску
                }
                else run.RestoreState(state, ad.chapterIndex);
                campaign.Arcs.Add(run);
            }
            // Сейвы до итерации 20 арок не заводили вовсе — досеиваем недостающие,
            // иначе старая кампания навсегда осталась бы без личных историй.
            campaign.SeedArcs(catalog);

            // Трофеи перебежчиков (v2): гир восстанавливается точно, оружие — из гира.
            foreach (var an in data.antagonists)
            {
                var rec = new AntagonistRecord { CompanionId = an.companionId, Level = an.level };
                foreach (var it in an.gear)
                {
                    var inst = RestoreItem(it, catalog);
                    if (inst == null) continue;
                    rec.CapturedGear.Add(inst);
                    if (rec.Weapon == null && inst.Weapon != null) rec.Weapon = inst.Weapon;
                }
                campaign.Antagonists.Add(rec);
            }

            return campaign;
        }

        /// <summary>
        /// Восстанавливает идущий прогон квеста/главы арки (v6) на сохранённом
        /// этапе. Контента нет (пул/арка изменились) — прогон не оживает, и квест
        /// уходит в «доступные» обычной демоцией: полусостояние хуже перепрохождения.
        /// </summary>
        private static void RestoreActiveQuest(SaveData data, Campaign campaign, ContentCatalog catalog)
        {
            if (string.IsNullOrEmpty(data.activeQuestId)) return;

            string arcId = string.IsNullOrEmpty(data.activeArcId) ? null : data.activeArcId;
            QuestDefinition def = null;
            if (arcId != null)
            {
                var arc = catalog.GetArc(arcId);
                if (arc != null)
                    foreach (var chapter in arc.Chapters)
                        if (chapter.Quest != null && chapter.Quest.Id == data.activeQuestId)
                        { def = chapter.Quest; break; }
            }
            else
            {
                foreach (var q in DefaultQuests.FullPool())
                    if (q.Id == data.activeQuestId) { def = q; break; }
            }
            if (def == null) return;

            // Индекс из сейва может указывать в никуда, если этапы квеста изменились
            // между версиями контента — тогда прогон не оживляем.
            if (def.StageAt(data.activeQuestIndex) == null) return;

            var run = new QuestRun(def, campaign.Base, campaign.Cfg, campaign.Factions,
                                   campaign.Base.ThreatsSystem, campaign.Flags);
            run.RestoreTo(data.activeQuestIndex, (QuestState)data.activeQuestState);
            campaign.SetActiveQuest(run, arcId);
        }

        private static Companion RestoreCompanion(CompanionDto cd, BalanceConfig cfg, ContentCatalog catalog)
        {
            var attrs = new AttributeBlock(cd.strength, cd.agility, cd.wits, cd.will);
            var c = new Companion(cd.id, attrs, cfg.TraitSlots)
            {
                DisplayName = cd.displayName,
                IsProtagonist = cd.isProtagonist
            };

            foreach (var sd in cd.skills) c.Skills.Set((SkillType)sd.skill, sd.level);
            foreach (var tid in cd.traitIds)
            {
                var t = catalog.GetTrait(tid);
                if (t != null) c.Traits.TryAdd(t);
            }
            foreach (var sid in cd.scarIds)
            {
                var s = catalog.GetScar(sid);
                if (s != null) c.Scars.Add(s);
            }
            foreach (var it in cd.equipment)
            {
                var inst = RestoreItem(it, catalog);
                if (inst != null) c.Equipment.Equip(inst);
            }

            c.RestoreProgress(cd.level, cd.xp, cd.unspentSkillPoints, (CompanionStatus)cd.status,
                              cd.loyalty, (InjuryTier)cd.injuryTier, cd.recoveryDays);

            // Перки — производная скилов: не хранятся в сейве, пересчитываются из каталога.
            c.RefreshPerks(catalog.Perks);
            return c;
        }

        private static ItemInstance RestoreItem(ItemDto it, ContentCatalog catalog)
        {
            var def = catalog.GetItem(it.defId);
            if (def == null) return null;
            var mods = new List<StatModifier>();
            for (int i = 0; i < it.mods.Count; i++)
            {
                var m = it.mods[i];
                mods.Add(new StatModifier((DerivedStat)m.stat, m.value, (ModMode)m.mode, ModifierSource.Gear));
            }
            return ItemInstance.FromSaved(def, (Rarity)it.rarity, mods);
        }
    }
}
