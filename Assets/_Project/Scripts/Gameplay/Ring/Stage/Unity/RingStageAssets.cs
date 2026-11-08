using UnityEngine;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Посилання на ассети 3D-рингу (арена, тіла борців, текстури, кліпи, мікрофон). Заповнює збирач сцени
    /// (<c>Editor/RingStageSceneSetup</c>): FBX і PNG лежать у Assets/Art, а в білд потрапляють саме через ці поля.
    /// Не лінтується (як і вся тека Stage/Unity).
    /// </summary>
    public sealed class RingStageAssets : MonoBehaviour
    {
        public GameObject venue;
        public GameObject bodyPlayer;
        public GameObject bodyRival;
        public GameObject bodyRef;
        public GameObject mic;
        /// <summary>Речі модульного набору (kit_m): одяг рефері, зачіски борців (<see cref="RingDress"/>).</summary>
        public GameObject kitMale;
        public GameObject kitFemale;

        /// <summary>
        /// Усі 16 тіл набору (body_&lt;стать&gt;_&lt;культура&gt;) і їхні текстури борця (ring_wrestler.py) — для героя з
        /// екрана створення й NPC кар'єри; індекси збігаються.
        /// </summary>
        public GameObject[] bodies = new GameObject[0];
        public Texture2D[] bodyAlbedo = new Texture2D[0];
        public Texture2D[] bodyNormal = new Texture2D[0];
        public Texture2D[] bodyMask = new Texture2D[0];

        /// <summary>Реквізит (стільці), натовп і його анімації, тексти зали (якорі канатів, списки стільців і фанатів).</summary>
        public GameObject props;
        public GameObject crowd;
        public AnimationClip[] crowdClips = new AnimationClip[0];
        public Avatar crowdAvatar;
        public TextAsset venueAnchors, venueProps, venueCrowd;

        /// <summary>Порядок: 0 — гравець, 1 — суперник, 2 — рефері.</summary>
        public Texture2D[] albedo = new Texture2D[3];
        public Texture2D[] normal = new Texture2D[3];
        public Texture2D[] mask = new Texture2D[3];

        /// <summary>Шаблон матеріала борця (URP/Lit); клонується на кожну постать.</summary>
        public Material wrestlerTemplate;

        /// <summary>Шаблон екрана (URP/Lit з емісією) — для титантрона.</summary>
        public Material screenTemplate;

        /// <summary>Матеріали частинок (URP Particles/Unlit): адитивний — іскри й піро, прозорий — CO2 і конфеті.</summary>
        public Material particleAdd, particleAlpha;

        /// <summary>Усі кліпи RING_Moves і RING_Promo (за іменем із маніфесту).</summary>
        public AnimationClip[] clips = new AnimationClip[0];

        /// <summary>Звук рингу (CC0, Assets/ThirdParty/CC0/Audio/Ring — LICENSES.md).</summary>
        public AudioClip musicTitle, musicHero, musicHammer, musicRoster;
        public AudioClip sndCrowd, sndApplause, sndBell, sndBellEnd, sndGong, sndMatSlap, sndLand;
        public AudioClip[] sndHit = new AudioClip[0];
        public AudioClip[] sndSlam = new AudioClip[0];

        public TextAsset manifest;
        public TextAsset promoManifest;
        public TextAsset entranceManifest;

        /// <summary>Мітки подій кліпів (контакт, канат, відлік рефері): чоловічий і жіночий набори — <see cref="RingClipEvents"/>.</summary>
        public TextAsset events;
        public TextAsset eventsF;

        /// <summary>Зсув кореня кліпів від першого до останнього кадру в одиницях ригу (ring_anims_motion.txt): масштаб тіла для бігу в канати.</summary>
        public TextAsset motion;
    }
}
