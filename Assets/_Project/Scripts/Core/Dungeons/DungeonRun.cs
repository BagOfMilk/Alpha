using System;
using System.Collections.Generic;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Checks;
using Game.Core.Combat;
using Game.Core.Economy;
using Game.Core.Items;

namespace Game.Core.Dungeons
{
    public enum DungeonOutcome
    {
        InProgress = 0,
        Extracted = 1, // вышли с добычей — забанковано
        Wiped = 2      // отряд пал — незабанкованное потеряно
    }

    /// <summary>Итог прохождения одной комнаты — для лога/UI.</summary>
    public sealed class RoomResolution
    {
        public RoomType Type;
        public string RoomName;
        public bool CheckResolved;
        public bool CheckSuccess;
        public int GainedGold, GainedBuilding, GainedCrafting, ItemsGained;
        public string Note;
    }

    /// <summary>Что вынесли при экстракте (материалы банкуются в леджер, предметы — в отчёт).</summary>
    public sealed class DungeonExtractReport
    {
        public int Gold, Building, Crafting, DepthReached;
        public readonly List<ItemInstance> Items = new List<ItemInstance>();
    }

    /// <summary>
    /// Push-your-luck прогон данжа (Эпик 12, US-12.2). Лут/материалы копятся в
    /// «незабанкованное» и зачисляются ТОЛЬКО при экстракте; уход глубже растит
    /// угрозу (опаснее враги/элита); вайп (поражение в бою) теряет незабанкованное.
    /// Бой ведёт вызывающий код (CombatState) и сообщает исход через ReportCombat;
    /// остальные комнаты резолвит ResolveRoom. RNG инъецируется (детерминизм).
    /// </summary>
    public sealed class DungeonRun
    {
        private readonly DungeonGenerator _gen;
        private readonly IRng _rng;

        public int Depth { get; private set; }
        public int Threat { get; private set; }
        public DungeonOutcome Outcome { get; private set; } = DungeonOutcome.InProgress;
        public DungeonRoom CurrentRoom { get; private set; }
        public bool CurrentCleared { get; private set; }

        public int UnbankedGold { get; private set; }
        public int UnbankedBuilding { get; private set; }
        public int UnbankedCrafting { get; private set; }
        public readonly List<ItemInstance> UnbankedItems = new List<ItemInstance>();

        public DungeonRun(DungeonGenerator generator, IRng rng)
        {
            _gen = generator ?? throw new ArgumentNullException(nameof(generator));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            Push(); // входим в первую комнату (глубина 1)
        }

        public bool Active => Outcome == DungeonOutcome.InProgress;

        /// <summary>Уйти глубже: новая комната, рост угрозы. Текущая должна быть пройдена.</summary>
        public DungeonRoom Push()
        {
            if (Outcome != DungeonOutcome.InProgress)
                throw new InvalidOperationException("Прогон завершён");
            if (CurrentRoom != null && !CurrentCleared)
                throw new InvalidOperationException("Текущая комната не пройдена");

            Depth++;
            Threat += _gen.ThreatPerDepth;
            CurrentRoom = _gen.NextRoom(Depth, Threat, _rng);
            CurrentCleared = false;
            return CurrentRoom;
        }

        /// <summary>Исход боя из текущей боевой комнаты (бой ведёт CombatState снаружи).</summary>
        public RoomResolution ReportCombat(bool squadWon)
        {
            RequireResolvable(RoomType.Combat);
            var res = new RoomResolution { Type = RoomType.Combat, RoomName = CurrentRoom.DisplayName };

            if (!squadWon)
            {
                Outcome = DungeonOutcome.Wiped;
                ClearUnbanked(); // незабанкованное потеряно (US-12.2)
                res.Note = "вайп — добыча потеряна";
                return res;
            }

            AddLoot(CurrentRoom.LootGold, CurrentRoom.LootBuilding, CurrentRoom.LootCrafting, res);
            CurrentCleared = true;
            return res;
        }

        /// <summary>Резолв небоевой комнаты (лут/проверка/событие) силами отряда.</summary>
        public RoomResolution ResolveRoom(IReadOnlyList<Companion> squad)
        {
            if (Outcome != DungeonOutcome.InProgress) throw new InvalidOperationException("Прогон завершён");
            if (CurrentRoom == null || CurrentCleared) throw new InvalidOperationException("Нечего резолвить");
            if (CurrentRoom.Type == RoomType.Combat)
                throw new InvalidOperationException("Боевую комнату резолвит ReportCombat");

            var room = CurrentRoom;
            var res = new RoomResolution { Type = room.Type, RoomName = room.DisplayName };

            switch (room.Type)
            {
                case RoomType.Loot:
                    AddLoot(room.LootGold, room.LootBuilding, room.LootCrafting, res);
                    RollItems(room, res);
                    break;

                case RoomType.Check:
                {
                    var check = CheckResolver.Resolve(squad, room.CheckSkill, room.CheckThreshold);
                    res.CheckResolved = true;
                    res.CheckSuccess = check.Success;
                    if (check.Success) AddLoot(room.CheckRewardGold, 0, 0, res);
                    else { Threat += room.EventThreatDelta; res.Note = "провал — дальше опаснее"; } // мягкий сетбэк
                    break;
                }

                case RoomType.Event:
                    if (room.EventLootGold > 0) AddLoot(room.EventLootGold, 0, 0, res);
                    if (room.EventThreatDelta > 0) { Threat += room.EventThreatDelta; res.Note = room.DisplayName; }
                    break;
            }

            CurrentCleared = true;
            return res;
        }

        /// <summary>Экстракт: банкует материалы в леджер, предметы — в отчёт; завершает прогон.</summary>
        public DungeonExtractReport Extract(BaseState baseState)
        {
            if (Outcome != DungeonOutcome.InProgress)
                throw new InvalidOperationException("Экстракт невозможен: прогон завершён");

            var rep = new DungeonExtractReport
            {
                Gold = UnbankedGold,
                Building = UnbankedBuilding,
                Crafting = UnbankedCrafting,
                DepthReached = Depth
            };
            rep.Items.AddRange(UnbankedItems);

            if (baseState != null)
            {
                baseState.Resources.Add(ResourceType.Gold, UnbankedGold);
                baseState.Resources.Add(ResourceType.BuildingMaterial, UnbankedBuilding);
                baseState.Resources.Add(ResourceType.CraftingMaterial, UnbankedCrafting);
            }

            ClearUnbanked();
            Outcome = DungeonOutcome.Extracted;
            return rep;
        }

        // ---- Внутренности ----
        private void RequireResolvable(RoomType expected)
        {
            if (Outcome != DungeonOutcome.InProgress) throw new InvalidOperationException("Прогон завершён");
            if (CurrentRoom == null || CurrentCleared) throw new InvalidOperationException("Нечего резолвить");
            if (CurrentRoom.Type != expected) throw new InvalidOperationException($"Комната не {expected}");
        }

        private void AddLoot(int gold, int building, int crafting, RoomResolution res)
        {
            UnbankedGold += gold; UnbankedBuilding += building; UnbankedCrafting += crafting;
            res.GainedGold += gold; res.GainedBuilding += building; res.GainedCrafting += crafting;
        }

        private void RollItems(DungeonRoom room, RoomResolution res)
        {
            if (room.ItemTable == null) return;
            for (int i = 0; i < room.ItemDrops; i++)
            {
                var item = LootGenerator.Roll(room.ItemTable, _rng);
                if (item != null) { UnbankedItems.Add(item); res.ItemsGained++; }
            }
        }

        private void ClearUnbanked()
        {
            UnbankedGold = UnbankedBuilding = UnbankedCrafting = 0;
            UnbankedItems.Clear();
        }
    }
}
