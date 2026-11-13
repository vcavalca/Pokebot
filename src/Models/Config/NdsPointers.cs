using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;

namespace Pokebot.Models.Config
{
    /// <summary>
    /// Memory layout of a DS game (one entry per game/language in Configs/Games).
    /// Absolute values are ARM9 bus addresses, the others are offsets from <see cref="Anchor"/>'s value
    /// (or from <see cref="FoeAnchor"/>'s value for the foe fields). Source: wyanido/pokebot-nds.
    /// Gen 5 has no anchor (<see cref="Anchor"/> = 0): its game data doesn't move, so every value is absolute.
    /// </summary>
    public class NdsPointers
    {
        /// <summary>
        /// Address holding the pointer to the main save data block (absolute, Gen 4).
        /// 0 for Gen 5: the "offsets" are then absolute addresses.
        /// </summary>
        [JsonConverter(typeof(HexConverter))]
        public long Anchor { get; set; }

        /// <summary>Address that stays 0 until a save is loaded (absolute, DPPt only).</summary>
        [JsonConverter(typeof(HexConverter))]
        public long SaveLoaded { get; set; }

        [JsonConverter(typeof(HexConverter))]
        public long PartyCount { get; set; }

        [JsonConverter(typeof(HexConverter))]
        public long Party { get; set; }

        /// <summary>
        /// 3DS: bytes between two party slots (0 = the party size, slots are contiguous). Ultra Sun/Moon keep each
        /// party Pokemon in its own heap object: 0x1E4 bytes apart.
        /// </summary>
        [JsonConverter(typeof(HexConverter))]
        public long PartySlotStride { get; set; }

        /// <summary>
        /// 3DS: offset of the 28 bytes of party stats (level, HP...) from the start of a slot: 0xE8 (right after the
        /// 232 bytes Pokemon) in X/Y, in another heap block in Ultra Sun/Moon.
        /// </summary>
        [JsonConverter(typeof(HexConverter))]
        public long PartyStats { get; set; } = 0xE8;

        [JsonConverter(typeof(HexConverter))]
        public long TrainerName { get; set; }

        /// <summary>TID (u16) followed by SID (u16).</summary>
        [JsonConverter(typeof(HexConverter))]
        public long TrainerId { get; set; }

        [JsonConverter(typeof(HexConverter))]
        public long MapHeader { get; set; }

        [JsonConverter(typeof(HexConverter))]
        public long Facing { get; set; }

        /// <summary>
        /// Values of the <see cref="Facing"/> byte for Up, Down, Left, Right: 0/1/2/3 in Gen 4,
        /// 0x00/0x80/0x40/0xC0 in Gen 5 (high byte of an angle).
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<int> FacingValues { get; set; } = new List<int> { 0, 1, 2, 3 };

        /// <summary>Offset from the anchor of the pointer to the battle data (0 = the foe values are absolute, Gen 5).</summary>
        [JsonConverter(typeof(HexConverter))]
        public long FoeAnchor { get; set; }

        /// <summary>Offset from the foe anchor.</summary>
        [JsonConverter(typeof(HexConverter))]
        public long FoeCount { get; set; }

        /// <summary>Offset from the foe anchor.</summary>
        [JsonConverter(typeof(HexConverter))]
        public long Foe { get; set; }

        /// <summary>
        /// 3DS: battle copy of the player's Pokemon. Like <see cref="Foe"/>, it only holds a valid Pokemon during a battle.
        /// </summary>
        [JsonConverter(typeof(HexConverter))]
        public long PlayerBattler { get; set; }

        /// <summary>
        /// Ultra Sun/Moon, absolute: u8 that is non-zero during a battle. Needed there because the battle copies stay
        /// valid after the battle (the foe slot is only cleared when the next battle starts).
        /// </summary>
        [JsonConverter(typeof(HexConverter))]
        public long BattleFlag { get; set; }

        /// <summary>
        /// Buttons that select RUN from any cursor position (null = Down, Down, Left, Left, Right, A). Ultra Sun/Moon:
        /// Down, Down, Down, A (RUN is the bottom button).
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string>? RunSequence { get; set; }

        /// <summary>The player walks with the Circle Pad (Ultra Sun/Moon: the D-pad doesn't walk).</summary>
        public bool AnalogMovement { get; set; }

        /// <summary>
        /// Battle menu state: 1 = main menu (FIGHT/BAG/RUN/POKEMON), see <see cref="BattleSubMenus"/>.
        /// Offset from the anchor, or from <see cref="BattleMenuAnchor"/>'s value when it is set.
        /// </summary>
        [JsonConverter(typeof(HexConverter))]
        public long BattleMenuState { get; set; }

        /// <summary>
        /// Gen 5 / Omega Ruby / Alpha Sapphire: absolute address of the pointer that <see cref="BattleMenuState"/> is
        /// relative to (on 3DS the pointer is a virtual address, so the offset is negative).
        /// </summary>
        [JsonConverter(typeof(HexConverter))]
        public long BattleMenuAnchor { get; set; }

        /// <summary>
        /// Battle menu states of the sub menus (moves, bag, party) that B closes: DPPt use 3/7/9 (4 while a move is
        /// chosen), HGSS 4/8/10, BW/B2W2 2 (moves; the bag reads 0 like the battle texts).
        /// </summary>
        // Replace: without it Json.NET would add the configured values to the default ones
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<int> BattleSubMenus { get; set; } = new List<int> { 3, 4, 7, 9 };

        /// <summary>Absolute.</summary>
        [JsonConverter(typeof(HexConverter))]
        public long BattleIndicator { get; set; }

        /// <summary>Absolute, fixed point (16.16).</summary>
        [JsonConverter(typeof(HexConverter))]
        public long PlayerX { get; set; }

        /// <summary>Absolute, fixed point (16.16).</summary>
        [JsonConverter(typeof(HexConverter))]
        public long PlayerY { get; set; }

        /// <summary>Absolute, fixed point (16.16).</summary>
        [JsonConverter(typeof(HexConverter))]
        public long PlayerZ { get; set; }

        /// <summary>
        /// Absolute: state of the main LCRNG (seed * 0x41C64E6D + 0x6073). It only moves when the game draws
        /// numbers (encounters, generated Pokemon...), so writing it after loading a state changes the result.
        /// </summary>
        [JsonConverter(typeof(HexConverter))]
        public long Rng { get; set; }

        /// <summary>
        /// Bytes of the <see cref="Rng"/> state: 4 in Gen 4, 8 in Gen 5 (64 bits LCRNG,
        /// seed * 0x5D588B656C078965 + 0x269EC3, followed in memory by its multiplier and increment).
        /// </summary>
        public int RngSize { get; set; } = 4;

        /// <summary>
        /// Absolute: Mersenne Twister state (624 u32), used for the Day-Care egg PID (the egg PID is its first output
        /// after a seeding, verified in Platinum) and for the IVs in Gen 5. Seeded at boot like the LCRNG
        /// (Gen 5: with the upper half of the boot seed).
        /// </summary>
        [JsonConverter(typeof(HexConverter))]
        public long Mt { get; set; }

        /// <summary>Absolute: Mersenne Twister index (624 = the next number twists the state).</summary>
        [JsonConverter(typeof(HexConverter))]
        public long MtIndex { get; set; }

        /// <summary>
        /// Index written with a new MT state: 624 for the DS games (a full twist comes first), 0 for X/Y whose MT
        /// twists each word when it is used (index u32 right before the state).
        /// </summary>
        public int MtSeedIndex { get; set; } = 624;

        /// <summary>Day-Care: 2 slots of <see cref="DaycareSlotSize"/> bytes (box Pokemon, mail..., u32 steps at the end).</summary>
        [JsonConverter(typeof(HexConverter))]
        public long Daycare { get; set; }

        [JsonConverter(typeof(HexConverter))]
        public long DaycareSlotSize { get; set; }

        /// <summary>
        /// Offset of the Pokemon in a Day-Care slot: 0 in Gen 4, 4 in Gen 5 (u32 "occupied" flag, then the 220 bytes
        /// party Pokemon, then u32 experience), 8 in Omega Ruby/Alpha Sapphire (u32 "occupied", u32 steps, then the
        /// 232 bytes Pokemon).
        /// </summary>
        [JsonConverter(typeof(HexConverter))]
        public long DaycarePokemonOffset { get; set; }

        /// <summary>
        /// Day-Care: PID of the egg the Day-Care Man has in Gen 4, "egg waiting" flag in Gen 5 and Omega Ruby/Alpha
        /// Sapphire (u32, 0 = no egg).
        /// </summary>
        [JsonConverter(typeof(HexConverter))]
        public long DaycareOffspring { get; set; }

        /// <summary>
        /// B2W2 / Omega Ruby / Alpha Sapphire: 64 bits egg seed kept by the Day-Care (0 for the other games). Written
        /// with random values with the RNG states before taking an egg.
        /// </summary>
        [JsonConverter(typeof(HexConverter))]
        public long DaycareEggSeed { get; set; }

        /// <summary>Item id registered on Y (u16).</summary>
        [JsonConverter(typeof(HexConverter))]
        public long RegisteredItem { get; set; }

        /// <summary>HGSS: second registered item (u16, 0 for DPPt which only have one).</summary>
        [JsonConverter(typeof(HexConverter))]
        public long RegisteredItem2 { get; set; }

        /// <summary>
        /// BW: list of registered key items, one byte per registered item (the game's own index, not the item id),
        /// <see cref="RegisteredSlotEmpty"/> in the unused bytes. 0 when unknown (B2W2): the registered items
        /// can't be checked then.
        /// </summary>
        [JsonConverter(typeof(HexConverter))]
        public long RegisteredSlots { get; set; }

        public int RegisteredSlotCount { get; set; }

        public int RegisteredSlotEmpty { get; set; }

        /// <summary>Item id of each <see cref="RegisteredSlots"/> index (0 = unknown index).</summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<int> RegisteredSlotItems { get; set; } = new List<int>();

        /// <summary>Key items pocket: u16 item id + u16 count per slot.</summary>
        [JsonConverter(typeof(HexConverter))]
        public long KeyItems { get; set; }

        /// <summary>
        /// Absolute: <see cref="FishingBiteValue"/> when a Pokemon bites while fishing. Gen 4: another non-zero value
        /// when nothing bites; Gen 5: see <see cref="FishingNoBite"/>.
        /// </summary>
        [JsonConverter(typeof(HexConverter))]
        public long FishingBite { get; set; }

        /// <summary>Value of <see cref="FishingBite"/> on a bite: 1 (u8) in Gen 4, 0xFFF1 (u16) in Gen 5.</summary>
        [JsonConverter(typeof(HexConverter))]
        public long FishingBiteValue { get; set; } = 1;

        /// <summary>Gen 5, absolute: non-zero (u8) once nothing bit ("Not even a nibble...").</summary>
        [JsonConverter(typeof(HexConverter))]
        public long FishingNoBite { get; set; }

        /// <summary>
        /// Omega Ruby/Alpha Sapphire: absolute address of a pointer that is 0 except while a Pokemon bites (it points
        /// to the bite's script object, allocated at another place for each bite). <see cref="FishingBite"/> is then an
        /// offset from the pointer's value (negative on 3DS: virtual address to FCRAM) to a byte that is 1 while the
        /// Pokemon can be hooked and 2 once it got away. Nothing biting leaves the pointer at 0.
        /// </summary>
        [JsonConverter(typeof(HexConverter))]
        public long FishingBiteAnchor { get; set; }

        /// <summary>
        /// Frames of the cast animation, during which the bite flag isn't trusted (null = the runner's 60).
        /// Omega Ruby/Alpha Sapphire: the bite pointer also points to the 3 objects of the cast animation (until ~100).
        /// </summary>
        public int? FishingCastFrames { get; set; }

        /// <summary>
        /// Frames after Y without any flag meaning that nothing bit (null = the runner's 360, HGSS). Omega Ruby/Alpha
        /// Sapphire signal "nothing" themselves (flag 2) and bite as late as ~380 frames, so they wait longer.
        /// </summary>
        public int? FishingNoBiteFrames { get; set; }

        /// <summary>
        /// Frames between the "nothing" flag and the B presses that close its text (null = 20). Omega Ruby/Alpha
        /// Sapphire open "Nothing seems to be biting..." ~30 frames after the flag: earlier presses are lost and the
        /// next cast is ignored.
        /// </summary>
        public int? FishingTextFrames { get; set; }

        /// <summary>
        /// Extra frames after closing the "nothing" text before the next cast (null = 0). Omega Ruby/Alpha Sapphire
        /// ignore a Y pressed right after the text closes.
        /// </summary>
        public int? FishingRecastFrames { get; set; }

        /// <summary>Starter briefcase / gift box: cursor position (0-2; Gen 5: 4 while no ball is highlighted).</summary>
        [JsonConverter(typeof(HexConverter))]
        public long StarterSelected { get; set; }

        /// <summary>
        /// Starter briefcase: the selection can be made when (value &amp; <see cref="StartersReadyMask"/>) == mask.
        /// Gen 5: 1 while the gift box choice is shown.
        /// </summary>
        [JsonConverter(typeof(HexConverter))]
        public long StartersReady { get; set; }

        [JsonConverter(typeof(HexConverter))]
        public long StartersReadyMask { get; set; }

        /// <summary>
        /// HGSS: the three starters (party format) are generated together when Elm's lab scene starts, at this offset
        /// (0 for DPPt, whose starter is generated when it is chosen in the briefcase).
        /// The u32 at -8 is 0 and the u32 at -4 non-zero once they are ready (wyanido/pokebot-nds).
        /// Ultra Sun/Moon (with <see cref="StarterSelected"/> set): the chosen starter (box format), valid once it is
        /// generated after "You chose X!", before it joins the party.
        /// </summary>
        [JsonConverter(typeof(HexConverter))]
        public long StarterData { get; set; }

        /// <summary>The starter choice is a vertical list (Up/Down, Ultra Sun/Moon) instead of a Left/Right one.</summary>
        public bool StarterMenuVertical { get; set; }

        /// <summary>1 while riding the bicycle.</summary>
        [JsonConverter(typeof(HexConverter))]
        public long Bike { get; set; }

        /// <summary>Bicycle gear, 1 is the fast one (DPPt only).</summary>
        [JsonConverter(typeof(HexConverter))]
        public long BikeGear { get; set; }
    }

    /// <summary>
    /// Reads "0x1234" / "-0x1234" strings (or plain numbers) as long.
    /// </summary>
    public class HexConverter : JsonConverter<long>
    {
        public override long ReadJson(JsonReader reader, Type objectType, long existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Integer)
            {
                return Convert.ToInt64(reader.Value, CultureInfo.InvariantCulture);
            }

            var text = ((string)reader.Value!).Trim();
            var negative = text.StartsWith("-", StringComparison.Ordinal);
            if (negative)
            {
                text = text.Substring(1);
            }

            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(2);
            }

            var value = long.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return negative ? -value : value;
        }

        public override void WriteJson(JsonWriter writer, long value, JsonSerializer serializer)
        {
            writer.WriteValue(value < 0 ? $"-0x{-value:X}" : $"0x{value:X}");
        }
    }
}
