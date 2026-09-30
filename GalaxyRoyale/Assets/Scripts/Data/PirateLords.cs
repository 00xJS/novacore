// Pirate Lords (late-game content, user-approved 2026-09-30). Ten named pirate
// warlords, each with a fleet doctrine and a lair on the map. The campaign
// (Data/Campaign) brings them on one chapter at a time; once beaten, they come
// back now and then with a bigger fleet (LairSystem rematches), so the late
// game always has a lord to hunt.
using System.Collections.Generic;

namespace GalaxyRoyale.Data
{
    public sealed class PirateLordDef
    {
        public int Index;
        public string Name = "";
        /// <summary>"the Rust Queen": shown after the name.</summary>
        public string Epithet = "";
        /// <summary>How their fleet fights, one line for the lair card.</summary>
        public string Doctrine = "";
        /// <summary>What they say when you find their lair, and when they fall.</summary>
        public string Boast = "", Last = "";
        /// <summary>Their fleet's mix: hull → share of the lair's budget.</summary>
        public IReadOnlyDictionary<HullId, float> Fleet = new Dictionary<HullId, float>();
        /// <summary>Portrait seed (Portraits.Avatar).</summary>
        public int Face;

        public string FullName => $"{Name}, {Epithet}";
    }

    public static class PirateLords
    {
        static PirateLordDef L(int i, string name, string epithet, string doctrine, string boast, string last,
            int face, params (HullId hull, float share)[] fleet)
        {
            var mix = new Dictionary<HullId, float>();
            foreach (var (h, s) in fleet) mix[h] = s;
            return new PirateLordDef
            {
                Index = i, Name = name, Epithet = epithet, Doctrine = doctrine, Boast = boast, Last = last,
                Face = face, Fleet = mix,
            };
        }

        public static readonly IReadOnlyList<PirateLordDef> All = new[]
        {
            L(0, "Grisha Vane", "the Rust Queen", "Swarms of cheap fighters. Bombers and cruisers cut them down",
                "Every ship you own will be scrap in my yard by nightfall.",
                "Keep the scrap. I'll be back for the rest.", 4101,
                (HullId.Fighter, 0.6f), (HullId.Bomber, 0.25f), (HullId.Talon, 0.15f)),
            L(1, "Old Marrow", "the Bone Collector", "Slow, heavily armoured hulls. Bring firepower, not speed",
                "I've picked cleaner bones than yours off the rim.",
                "Bones break. Mine took their time about it.", 4202,
                (HullId.Sentinel, 0.35f), (HullId.Rampart, 0.35f), (HullId.Bulwark, 0.3f)),
            L(2, "Kessa Nightjar", "the Unseen", "Fast raiders that strike first. Tough hulls outlast them",
                "You never saw me, Commander. You never will.",
                "So you can see in the dark. Remember that I let you.", 4303,
                (HullId.Harrier, 0.4f), (HullId.Javelin, 0.35f), (HullId.Wraith, 0.25f)),
            L(3, "Oro and Ash", "the Twin Admirals", "Two balanced fleets that cover each other's weaknesses",
                "Two admirals, one grudge. Choose which of us kills you.",
                "Ash always said you'd be trouble. Ash was right.", 4404,
                (HullId.Cruiser, 0.25f), (HullId.Vanguard, 0.25f), (HullId.Corsair, 0.25f), (HullId.Lancer, 0.25f)),
            L(4, "Brother Cinder", "the Flame Preacher", "Bomber wings and gunships. Interceptors eat them alive",
                "The fire is coming, Commander. I only carry the match.",
                "Ash to ash. You were the fire all along.", 4505,
                (HullId.Bomber, 0.45f), (HullId.Corsair, 0.3f), (HullId.Cruiser, 0.25f)),
            L(5, "The Hollow King", "Lord of Wrecks", "Capital ships: dreadnoughts at the heart of the fleet",
                "Kneel, and I'll let your colony be my throne room.",
                "A crown of wrecks. Wear it better than I did.", 4606,
                (HullId.Behemoth, 0.35f), (HullId.Leviathan, 0.35f), (HullId.Rampart, 0.3f)),
            L(6, "Vesper Lyse", "the Signal Witch", "Destroyer packs that hunt capital ships",
                "I've been listening to your fleet chatter for weeks.",
                "The signal goes quiet. For now.", 4707,
                (HullId.Reaper, 0.4f), (HullId.Warden, 0.3f), (HullId.Wraith, 0.3f)),
            L(7, "Warlord Dray", "the Iron Tide", "A tide of every hull class, and lots of it",
                "The tide doesn't argue, Commander. It arrives.",
                "Tides turn. I just didn't think it'd be you turning it.", 4808,
                (HullId.Vanguard, 0.2f), (HullId.Bulwark, 0.2f), (HullId.Behemoth, 0.2f), (HullId.Reaper, 0.2f),
                (HullId.Fighter, 0.2f)),
            L(8, "The Pale Herald", "Voice of the Court", "Elite hulls of every line, drilled to perfection",
                "The Sovereign sends greetings. And a funeral.",
                "The Court will hear of this. They'll come themselves.", 4909,
                (HullId.Leviathan, 0.25f), (HullId.Nomad, 0.25f), (HullId.Warden, 0.25f), (HullId.Javelin, 0.25f)),
            L(9, "The Pale Sovereign", "Last Night of the Galaxy", "Everything the lords had, and the Sovereign's own dreadnoughts",
                "I have outlived every empire in this galaxy. I will outlive yours.",
                "Then let there be a morning. It suits you better than me.", 5010,
                (HullId.Behemoth, 0.2f), (HullId.Leviathan, 0.2f), (HullId.Nomad, 0.2f), (HullId.Reaper, 0.15f),
                (HullId.Wraith, 0.15f), (HullId.Rampart, 0.1f)),
        };

        public static PirateLordDef Def(int index) => All[index];
    }
}
