using System;
using System.Collections.Generic;
using System.Linq;
using BizHawk.Client.Common;
using Pokebot.Models.Config;
using Pokebot.Models.Pokemons;

namespace Pokebot.Models.Memory
{
    /// <summary>
    /// Memory reader for the 3DS games (Gen 6: X / Y), on BizHawk's Encore core. Every pointer is an absolute offset
    /// in the FCRAM domain: the game heap was at the same place in every boot of our tests.
    /// Pokemon use the PK6 format (232 bytes, 260 in the party): the 0x08-0xE7 part is XORed with the LCRNG seeded by the
    /// encryption constant (0x00) and made of 4 blocks of 56 bytes shuffled by ((EC >> 13) &amp; 31) % 24; the party
    /// stats (0xE8-) are XORed with the same LCRNG seeded again by the encryption constant.
    /// https://projectpokemon.org/home/docs/gen-6/pkm-structure-r65/
    /// </summary>
    public class ThreeDsMemory : NdsMemory
    {
        public const int Pk6BoxSize = 232;
        public const int Pk6PartySize = 260;
        private const int BlockSize = 56;

        /// <summary>X / Y: map coordinates are floats, 18 units per tile.</summary>
        private const float TileSize = 18f;

        private static readonly string[] _blockOrders =
        {
            "ABCD", "ABDC", "ACBD", "ACDB", "ADBC", "ADCB",
            "BACD", "BADC", "BCAD", "BCDA", "BDAC", "BDCA",
            "CABD", "CADB", "CBAD", "CBDA", "CDAB", "CDBA",
            "DABC", "DACB", "DBAC", "DBCA", "DCAB", "DCBA",
        };

        public ThreeDsMemory(ApiContainer apiContainer, VersionInfo versionInfo, HashData hashData, GenerationInfo generationInfo)
            : base(apiContainer, versionInfo, hashData, generationInfo)
        {
            PartyPokemonSize = Pk6PartySize;
        }

        public override string Domain => Fcram;

        protected override int StoredPokemonSize => Pk6BoxSize;

        /// <summary>Turning in place doesn't trigger encounters in X/Y either (a 8-14 frames press walks one tile).</summary>
        public override bool WalksForEncounters => true;

        public override bool EggDecidedWhenReceived => true;

        protected override bool Utf16Text => true;

        #region State

        /// <summary>
        /// In game once the party count is 1-6 and the first party Pokemon is valid (the trainer data is in the heap
        /// from the title screen on, so it can't tell).
        /// </summary>
        public override bool IsSaveLoaded()
        {
            var count = ReadU8(Pointers.PartyCount);
            if (count < 1 || count > 6)
            {
                return false;
            }

            return TryReadPartySlot(0) != null;
        }

        #endregion

        #region Party

        private long SlotStride => Pointers.PartySlotStride != 0 ? Pointers.PartySlotStride : Pk6PartySize;

        /// <summary>
        /// Party Pokemon <paramref name="index"/>: the 232 bytes Pokemon and its 28 bytes of party stats, which are
        /// contiguous in X/Y and in two heap blocks in Ultra Sun/Moon (see <see cref="NdsPointers.PartyStats"/>).
        /// </summary>
        private Pokemon? TryReadPartySlot(int index)
        {
            var address = Pointers.Party + index * SlotStride;
            var data = new byte[Pk6PartySize];
            Array.Copy(ReadBytes(address, Pk6BoxSize), data, Pk6BoxSize);
            Array.Copy(ReadBytes(address + Pointers.PartyStats, Pk6PartySize - Pk6BoxSize), 0, data, Pk6BoxSize, Pk6PartySize - Pk6BoxSize);
            return TryParsePokemon(data, new MemoryLocation(address, 0, Pk6PartySize, Domain));
        }

        public override IReadOnlyList<Pokemon> GetParty()
        {
            var list = new List<Pokemon>();
            var count = GetPartyCount();
            for (int i = 0; i < count; i++)
            {
                var pokemon = TryReadPartySlot(i);
                if (pokemon != null)
                {
                    list.Add(pokemon);
                }
            }

            return list;
        }

        /// <summary>
        /// Ultra Sun/Moon: the starter chosen in the list, readable as soon as it is generated (before it joins the
        /// party). Omega Ruby/Alpha Sapphire: the first party slot, the starter joins the party when it is chosen
        /// (right before the Poochyena battle). Empty for the other games.
        /// </summary>
        public override IReadOnlyList<Pokemon> GetGeneratedStarters()
        {
            var list = new List<Pokemon>();
            if (Pointers.StarterData == 0)
            {
                return list;
            }

            var pokemon = TryParsePokemon(ReadBytes(Pointers.StarterData, Pk6BoxSize), new MemoryLocation(Pointers.StarterData, 0, Pk6BoxSize, Domain));
            if (pokemon != null)
            {
                list.Add(pokemon);
            }

            return list;
        }

        #endregion

        #region Battle

        /// <summary>
        /// The battle slots of the player's Pokemon and of the foe only hold valid Pokemon during a battle (other data
        /// uses that memory the rest of the time), so both checksums matching means a battle is running.
        /// </summary>
        /// <summary>
        /// Ultra Sun/Moon: encryption constant left in the foe slot by the last battle (the slot keeps it until the
        /// next battle clears it), so it isn't taken for the new foe when the battle flag rises first.
        /// </summary>
        private uint? _staleFoe;

        public override bool IsBattleRunning()
        {
            if (Pointers.BattleFlag != 0)
            {
                var foe = ReadU32(Pointers.Foe);
                if (ReadU8(Pointers.BattleFlag) == 0)
                {
                    _staleFoe = foe;
                    return false;
                }

                return foe != _staleFoe && IsValidPokemonAt(Pointers.Foe);
            }

            if (Pointers.PlayerBattler != 0 && !IsValidPokemonAt(Pointers.PlayerBattler))
            {
                return false;
            }

            return IsValidPokemonAt(Pointers.Foe);
        }

        private bool IsValidPokemonAt(long address)
        {
            var data = ReadBytes(address, Pk6BoxSize);
            return Decrypt6(data, out _, out _) != null;
        }

        /// <summary>Size of the FCRAM domain (the 3DS main memory).</summary>
        private const long FcramSize = 0x10000000;

        /// <summary>Start of the virtual addresses of the application heap (the pointers the games store).</summary>
        private const long HeapVirtualStart = 0x08000000;

        /// <summary>
        /// Omega Ruby/Alpha Sapphire: the battle data and the fishing bite object move, their anchors hold virtual
        /// addresses (0x08xxxxxx heap, 0x16xxxxxx script objects) and the configured offset is negative: it turns that
        /// address into an FCRAM offset. Otherwise the anchor holds 0 or an unrelated value.
        /// </summary>
        protected override bool IsDataPointer(long value, long offset)
        {
            var address = value + offset;
            return value >= HeapVirtualStart && address >= 0 && address < FcramSize;
        }

        /// <summary>The wild / trainer Pokemon in front of the player (box format: the battle copy has no party stats).</summary>
        public override Pokemon? TryGetOpponent()
        {
            var data = ReadBytes(Pointers.Foe, Pk6BoxSize);
            return TryParsePokemon(data, new MemoryLocation(Pointers.Foe, 0, Pk6BoxSize, Domain));
        }

        #endregion

        #region Player

        protected override int ReadTileCoordinate(long address)
        {
            var value = BitConverter.ToSingle(ReadBytes(address, 4), 0);
            return float.IsNaN(value) || float.IsInfinity(value) ? 0 : (int)Math.Floor(value / TileSize);
        }

        #endregion

        #region Pokemon

        /// <summary>
        /// Decrypts and parses a PK6 (232 bytes box / 260 bytes party). Returns null if the checksum doesn't match.
        /// </summary>
        public override Pokemon? TryParsePokemon(byte[] encrypted, MemoryLocation memoryLocation)
        {
            var data = Decrypt6(encrypted, out var checksum, out var calculatedChecksum);
            if (data == null)
            {
                return null;
            }

            #region Block A

            var species = BitConverter.ToUInt16(data, 0x08);
            var pokemonConfig = GenerationInfo.Pokemons.FirstOrDefault(x => x.Id == species);
            if (pokemonConfig == null)
            {
                return null;
            }

            var heldItemId = BitConverter.ToUInt16(data, 0x0A);
            var heldItem = heldItemId == 0 ? null : GenerationInfo.Items.FirstOrDefault(x => x.Id == heldItemId) ?? new PokemonItem(heldItemId, $"#{heldItemId}");
            var otId = BitConverter.ToUInt16(data, 0x0C);
            var otSecretId = BitConverter.ToUInt16(data, 0x0E);
            var experience = BitConverter.ToUInt32(data, 0x10);
            var abilityId = data[0x14];
            var pid = BitConverter.ToUInt32(data, 0x18);
            var natureId = data[0x1C];
            var genderFlags = data[0x1D] >> 1 & 0x3;
            var gender = (genderFlags & 0x2) != 0 ? PokemonGender.Genderless : (genderFlags & 0x1) != 0 ? PokemonGender.Female : PokemonGender.Male;
            var evs = new PokemonEVS(data[0x1E], data[0x1F], data[0x20], data[0x21], data[0x22], data[0x23]);
            var conditions = new PokemonCondition(data[0x24], data[0x25], data[0x26], data[0x27], data[0x28], data[0x29]);
            var markings = (PokemonMark)(data[0x2A] & 0xF);
            var pokerus = new PokemonPokerus(data[0x2B] & 0xF, data[0x2B] >> 4);

            #endregion

            #region Block B

            var nickname = DecodeString(data, 0x40, 12);
            var moves = new List<PokemonAttack>();
            for (int i = 0; i < 4; i++)
            {
                var moveId = BitConverter.ToUInt16(data, 0x5A + i * 2);
                if (moveId != 0)
                {
                    var move = GenerationInfo.Moves.FirstOrDefault(x => x.Id == moveId) ?? new PokemonMove(moveId, $"#{moveId}", "???", "???", 0, 0, 0);
                    moves.Add(new PokemonAttack(move, data[0x62 + i]));
                }
            }

            var ppBonuses = (int)BitConverter.ToUInt32(data, 0x66);
            var ivData = BitConverter.ToUInt32(data, 0x74);
            var ivHP = (int)(ivData & 0x1F);
            var ivAttack = (int)(ivData >> 5 & 0x1F);
            var ivDefense = (int)(ivData >> 10 & 0x1F);
            var ivSpeed = (int)(ivData >> 15 & 0x1F);
            var ivSpAttack = (int)(ivData >> 20 & 0x1F);
            var ivSpDefense = (int)(ivData >> 25 & 0x1F);
            var isEgg = (ivData >> 30 & 0x1) == 1;
            var ivs = new PokemonIVS(ivHP, ivAttack, ivDefense, ivSpeed, ivSpAttack, ivSpDefense);

            #endregion

            #region Block D

            var otName = DecodeString(data, 0xB0, 12);
            var friendship = (int)data[0xCA];
            var eggLocation = BitConverter.ToUInt16(data, 0xD8);
            var metLocationId = BitConverter.ToUInt16(data, 0xDA);
            var pokeballId = data[0xDC];
            var metLevel = data[0xDD] & 0x7F;
            var otIsMale = (data[0xDD] >> 7) == 0;
            var gameId = data[0xDF];
            var language = (PokemonLanguageCode)data[0xE3];

            #endregion

            #region Derived info

            var game = GenerationInfo.GameOfOrigin.FirstOrDefault(x => x.Id == gameId) ?? new PokemonGameOfOrigin(gameId, $"#{gameId}");
            var pokeball = GenerationInfo.Items.FirstOrDefault(x => x.Id == pokeballId);
            var origin = new PokemonOriginInfo(otIsMale, pokeball, game, eggLocation != 0);
            var metLocation = GenerationInfo.Locations.FirstOrDefault(x => x.Id == metLocationId) ?? new PokemonLocation(metLocationId, $"#{metLocationId}");
            var originalTrainer = new PokemonOriginalTrainer(otId, otSecretId, otName);
            var ability = abilityId < GenerationInfo.Abilities.Count ? GenerationInfo.Abilities[abilityId] : $"#{abilityId}";
            var types = GenerationInfo.Types.Where(x => pokemonConfig.Types.Any(y => y.Equals(x.Name, StringComparison.InvariantCultureIgnoreCase))).ToList();
            var nature = GenerationInfo.Natures.FirstOrDefault(x => x.Id == natureId);

            var hiddenPowerTypeIndex = (ivHP % 2 + 2 * (ivAttack % 2) + 4 * (ivDefense % 2) + 8 * (ivSpeed % 2) + 16 * (ivSpAttack % 2) + 32 * (ivSpDefense % 2)) * 15 / 63;
            var hiddenPower = GenerationInfo.HiddenPowers.FirstOrDefault(x => x.Id == hiddenPowerTypeIndex);
            var hiddenPowerDamage =
                (ivHP >> 1 & 1) + 2 * (ivAttack >> 1 & 1) + 4 * (ivDefense >> 1 & 1) + 8 * (ivSpeed >> 1 & 1) + 16 * (ivSpAttack >> 1 & 1) + 32 * (ivSpDefense >> 1 & 1);
            hiddenPowerDamage = hiddenPowerDamage * 40 / 63 + 30;

            // Gen 6+: shiny below 16 (8 before)
            var shinyValue = otId ^ otSecretId ^ (int)(pid >> 16) ^ (int)(pid & 0xFFFF);
            var shiny = shinyValue < 16;

            #endregion

            if (encrypted.Length >= Pk6PartySize)
            {
                var status = (PokemonStatusType)data[0xE8];
                var level = (int)data[0xEC];
                var currentHP = BitConverter.ToUInt16(data, 0xF0);
                var maxHP = BitConverter.ToUInt16(data, 0xF2);
                var attack = BitConverter.ToUInt16(data, 0xF4);
                var defense = BitConverter.ToUInt16(data, 0xF6);
                var speed = BitConverter.ToUInt16(data, 0xF8);
                var spAttack = BitConverter.ToUInt16(data, 0xFA);
                var spDefense = BitConverter.ToUInt16(data, 0xFC);

                return new PartyPokemon(
                    pid, originalTrainer, nickname, language, PokemonEggNameType.None, markings, checksum, calculatedChecksum,
                    species, heldItem, experience, ppBonuses, friendship, moves, evs, conditions, pokerus, metLocation, metLevel,
                    origin, ivs, isEgg, null, pokemonConfig.Name, pokemonConfig.DexId, shiny, nature, types, ability, hiddenPower,
                    hiddenPowerDamage, gender, status, level, pokerus.Strain != 0 && pokerus.Days != 0, currentHP, maxHP, attack,
                    defense, speed, spAttack, spDefense, memoryLocation
                );
            }

            return new Pokemon(
                pid, originalTrainer, nickname, language, PokemonEggNameType.None, markings, checksum, calculatedChecksum,
                species, heldItem, experience, ppBonuses, friendship, moves, evs, conditions, pokerus, metLocation, metLevel,
                origin, ivs, isEgg, null, pokemonConfig.Name, pokemonConfig.DexId, shiny, nature, types, ability, hiddenPower,
                hiddenPowerDamage, gender, memoryLocation
            );
        }

        /// <summary>
        /// Returns the decrypted PK6 with the blocks back in A/B/C/D order, or null if it isn't a valid Pokemon.
        /// </summary>
        public static byte[]? Decrypt6(byte[] encrypted, out int checksum, out int calculatedChecksum)
        {
            checksum = 0;
            calculatedChecksum = 0;
            if (encrypted.Length < Pk6BoxSize)
            {
                return null;
            }

            var encryptionConstant = BitConverter.ToUInt32(encrypted, 0x00);
            var sanity = BitConverter.ToUInt16(encrypted, 0x04);
            checksum = BitConverter.ToUInt16(encrypted, 0x06);
            if (encryptionConstant == 0 || sanity != 0)
            {
                return null;
            }

            var decrypted = (byte[])encrypted.Clone();
            XorWithPrng(decrypted, 0x08, Pk6BoxSize, encryptionConstant);

            var sum = 0;
            for (int i = 0x08; i < Pk6BoxSize; i += 2)
            {
                sum += BitConverter.ToUInt16(decrypted, i);
            }

            calculatedChecksum = sum & 0xFFFF;
            if (calculatedChecksum != checksum)
            {
                return null;
            }

            var order = _blockOrders[((encryptionConstant >> 13) & 31) % 24];
            var result = (byte[])decrypted.Clone();
            for (int i = 0; i < 4; i++)
            {
                var target = order[i] - 'A';
                Array.Copy(decrypted, 0x08 + i * BlockSize, result, 0x08 + target * BlockSize, BlockSize);
            }

            if (encrypted.Length > Pk6BoxSize)
            {
                // The party stats use the LCRNG seeded again by the encryption constant
                XorWithPrng(result, Pk6BoxSize, encrypted.Length, encryptionConstant);
            }

            return result;
        }

        private static void XorWithPrng(byte[] data, int start, int end, uint seed)
        {
            for (int i = start; i + 1 < end; i += 2)
            {
                seed = unchecked(seed * 0x41C64E6D + 0x6073);
                var value = BitConverter.ToUInt16(data, i) ^ (seed >> 16);
                data[i] = (byte)(value & 0xFF);
                data[i + 1] = (byte)(value >> 8 & 0xFF);
            }
        }

        #endregion
    }
}
