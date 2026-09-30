namespace Game.Core.Combat
{
    /// <summary>
    /// Об'єкт поля бою (Поправка №14.4; власник, 29.09.2026: «ти не додав
    /// взагалі укриття та взриваючі бочки чи щось таке»). Займає клітинку
    /// (непрохідну) і дає укриття сусідам з того боку, де стоїть, — як у
    /// тактиках із загоном: ховаєшся ЗА об'єктом, а не в ньому.
    /// </summary>
    public enum MapObjectKind
    {
        /// <summary>Низька перепона (тин, колоди) — ½ укриття сусідам; огляду не закриває.</summary>
        LowCover = 0,
        /// <summary>Висока перепона (брила, стіна з колод) — повне укриття сусідам; закриває огляд.</summary>
        HighCover = 1,
        /// <summary>Бочка з порохом — ½ укриття; від удару вибухає: шкода довкола, руйнує укриття, підриває сусідні бочки.</summary>
        PowderKeg = 2,
        /// <summary>Копиця сіна — ½ укриття; від вогню чи вибуху займається: довкола зона горіння на кілька раундів.</summary>
        Haystack = 3
    }

    /// <summary>Об'єкт на полі в бою. Руйнується поетапно: висока → низька → нічого (ніколи повністю за один удар).</summary>
    public sealed class MapObject
    {
        public string Id;
        public MapObjectKind Kind;
        public GridPos Pos;

        /// <summary>Укриття, яке об'єкт дає сусідам.</summary>
        public CoverType CoverForNeighbours => Kind == MapObjectKind.HighCover ? CoverType.Full : CoverType.Half;

        /// <summary>По ньому можна вдарити, щоб спрацював (бочка вибухне, сіно займеться).</summary>
        public bool IsTargetable => Kind == MapObjectKind.PowderKeg || Kind == MapObjectKind.Haystack;

        public bool BlocksSight => Kind == MapObjectKind.HighCover;
    }

    /// <summary>Де стоїть об'єкт на старті бою (BattleSetup.Objects).</summary>
    public readonly struct MapObjectPlacement
    {
        public readonly MapObjectKind Kind;
        public readonly GridPos Pos;

        public MapObjectPlacement(MapObjectKind kind, GridPos pos)
        {
            Kind = kind;
            Pos = pos;
        }
    }

    /// <summary>Зона вогню від спаленої копиці: хто стоїть у ній на початку свого ходу — горить.</summary>
    public sealed class FireZone
    {
        public GridPos Center;
        public int Radius;
        public int RoundsLeft;

        public bool Covers(GridPos p) => GridPos.Chebyshev(Center, p) <= Radius;
    }

    /// <summary>Підкріплення ворога: на якому раунді прийде і де стане (Поправка №14.4, правило контенту).</summary>
    public readonly struct ReinforcementSpawn
    {
        public readonly int Round;
        public readonly string EnemyDefinitionId;
        public readonly GridPos Pos;

        public ReinforcementSpawn(int round, string enemyDefinitionId, GridPos pos)
        {
            Round = round;
            EnemyDefinitionId = enemyDefinitionId;
            Pos = pos;
        }
    }
}
