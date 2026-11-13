using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BizHawk.Client.Common;
using Pokebot.Models.Config;
using Pokebot.Models.Player;
using Pokebot.Models.Pokemons;
using Pokebot.Utils;

namespace Pokebot.Models.Memory
{
    /// <summary>
    /// Memory reader for the DS games: Diamond / Pearl / Platinum / HeartGold / SoulSilver (Gen 4) and
    /// Black / White / Black 2 / White 2 (Gen 5).
    /// Gen 4 game data lives in the heap, so every read starts from the anchor pointer in <see cref="NdsPointers"/>;
    /// Gen 5 data doesn't move and its pointers are absolute addresses (base 0).
    /// </summary>
    public class NdsMemory : CommonGenMemory
    {
        public const int BoxPokemonSize = 136;

        /// <summary>Bytes of a Pokemon stored outside the party (Day-Care): the box format, 232 bytes on 3DS.</summary>
        protected virtual int StoredPokemonSize => BoxPokemonSize;

        /// <summary>Values of the battle indicator byte while a battle is running (wyanido/pokebot-nds).</summary>
        private static readonly byte[] _battleIndicatorValues = { 0x41, 0x97, 0xC0 };

        // Order of the A/B/C/D blocks for each shift value ((PID & 0x3E000) >> 0xD) % 24
        // https://projectpokemon.org/home/docs/gen-4/pkm-structure-r65/
        private static readonly string[] _blockOrders =
        {
            "ABCD", "ABDC", "ACBD", "ACDB", "ADBC", "ADCB",
            "BACD", "BADC", "BCAD", "BCDA", "BDAC", "BDCA",
            "CABD", "CADB", "CBAD", "CBDA", "CDAB", "CDBA",
            "DABC", "DACB", "DBAC", "DBCA", "DCAB", "DCBA",
        };

        public NdsPointers Pointers { get; }

        /// <summary>Gen 5: absolute addresses, 220 bytes party Pokemon, UTF-16 text, nature stored apart from the PID.</summary>
        public bool IsGen5 { get; }

        /// <summary>Party (and battle) Pokemon size: 236 bytes in Gen 4 (ball seals), 220 in Gen 5.</summary>
        public int PartyPokemonSize { get; protected set; }

        /// <summary>
        /// Turning in place doesn't trigger encounters (Gen 5 and the 3DS games): the Spin bot walks one tile and back.
        /// </summary>
        public virtual bool WalksForEncounters => IsGen5;

        /// <summary>
        /// The Day-Care egg is only decided when it is taken from the Day-Care Man (Gen 5 and the 3DS games), not when
        /// it is made (Gen 4).
        /// </summary>
        public virtual bool EggDecidedWhenReceived => IsGen5;

        /// <summary>Text is UTF-16 (Gen 5 and later), not an index in the Gen 4 character table.</summary>
        protected virtual bool Utf16Text => IsGen5;

        /// <summary>Consecutive reads where each party slot failed its checksum.</summary>
        private readonly int[] _invalidReads = new int[6];
        /// <summary>
        /// The game re-encrypts a party Pokemon when it changes it (every step, hatching...) and a frame can end
        /// in the middle of it: a slot is only reported once it stays invalid this many reads in a row.
        /// </summary>
        private const int InvalidReadsBeforeWarning = 120;

        public NdsMemory(ApiContainer apiContainer, VersionInfo versionInfo, HashData hashData, GenerationInfo generationInfo)
            : base(apiContainer, versionInfo, hashData, generationInfo)
        {
            Pointers = versionInfo.Pointers ?? throw new ArgumentException($"Missing pointers for {versionInfo.Name}");
            IsGen5 = versionInfo.Generation >= 5;
            PartyPokemonSize = IsGen5 ? 220 : 236;
        }

        #region Pointers

        /// <summary>
        /// Returns the address the pointer offsets are relative to (Gen 4: the save data block, Gen 5: 0),
        /// or null if the Gen 4 anchor isn't set yet. See <see cref="IsSaveLoaded"/> for the in-game check.
        /// </summary>
        public long? GetBase()
        {
            if (Pointers.Anchor == 0)
            {
                return 0;
            }

            long anchor = ReadU32(Pointers.Anchor);
            return IsMainRamPointer(anchor) ? anchor : (long?)null;
        }

        public virtual bool IsSaveLoaded()
        {
            var baseAddress = GetBase();
            if (baseAddress == null)
            {
                return false;
            }

            if (Pointers.Anchor == 0)
            {
                // Gen 5: the map and the trainer name are only set once a save is loaded (or a new game named)
                var firstLetter = ReadU16(Pointers.TrainerName);
                if (firstLetter == 0 || firstLetter == 0xFFFF)
                {
                    return false;
                }
            }

            // DPPt: stays 0 on the title screen and while the journal is shown (not set for HGSS)
            if (Pointers.SaveLoaded != 0 && ReadU32(Pointers.SaveLoaded) == 0)
            {
                return false;
            }

            var map = ReadU16(baseAddress.Value + Pointers.MapHeader);
            return map > 0 && map < 1000;
        }

        private long? GetFoeBase(long baseAddress)
        {
            if (Pointers.FoeAnchor == 0)
            {
                return baseAddress;
            }

            long foeAnchor = ReadU32(baseAddress + Pointers.FoeAnchor);
            return IsMainRamPointer(foeAnchor) ? foeAnchor : (long?)null;
        }

        #endregion

        #region State

        public override GameState GetGameState()
        {
            if (!IsSaveLoaded())
            {
                return GameState.TitleScreen;
            }

            if (IsBattleRunning())
            {
                return TryGetOpponent() != null ? GameState.Battle : GameState.StartBattle;
            }

            return GameState.Overworld;
        }

        public virtual bool IsBattleRunning()
        {
            return _battleIndicatorValues.Contains(ReadU8(Pointers.BattleIndicator));
        }

        /// <summary>
        /// Battle menu state: 1 = main menu (FIGHT/BAG/RUN/POKEMON), sub menus in <see cref="NdsPointers.BattleSubMenus"/>.
        /// </summary>
        public int GetBattleMenuState()
        {
            if (Pointers.BattleMenuAnchor != 0)
            {
                long battle = ReadU32(Pointers.BattleMenuAnchor);
                return IsDataPointer(battle, Pointers.BattleMenuState) ? ReadU8(battle + Pointers.BattleMenuState) : 0;
            }

            var baseAddress = GetBase();
            return baseAddress == null ? 0 : ReadU8(baseAddress.Value + Pointers.BattleMenuState);
        }

        /// <summary>
        /// Whether a value read at an anchor (<see cref="NdsPointers.BattleMenuAnchor"/>,
        /// <see cref="NdsPointers.FishingBiteAnchor"/>) points to game data, <paramref name="offset"/> being added to it.
        /// </summary>
        protected virtual bool IsDataPointer(long value, long offset)
        {
            return IsMainRamPointer(value);
        }

        public override int GetActionSelectionCursor()
        {
            return GetBattleMenuState();
        }

        public override void TrySetEscape()
        {
            // The DS games are driven with buttons, see NdsActionRunner.Escape
        }

        #endregion

        #region Player

        public override PlayerData GetPlayer()
        {
            var baseAddress = GetBase();
            var tile = GetTilePosition();
            var position = new Position(tile.X, tile.Z);
            var facing = baseAddress == null ? PlayerFacingDirection.Down : ToFacingDirection(ReadU8(baseAddress.Value + Pointers.Facing));
            var map = baseAddress == null ? 0 : ReadU16(baseAddress.Value + Pointers.MapHeader);
            var name = baseAddress == null ? string.Empty : ReadString(baseAddress.Value + Pointers.TrainerName, 8);

            return new PlayerData(position, position, PlayerRunningState.NotMoving, TileTransitionState.NotMoving, true, facing, 0, map, GetTID(), name);
        }

        /// <summary>Tile coordinates of the player (Z is the vertical axis of the map).</summary>
        public (int X, int Z) GetTilePosition()
        {
            return (ReadTileCoordinate(Pointers.PlayerX), ReadTileCoordinate(Pointers.PlayerZ));
        }

        /// <summary>DS: 16.16 fixed point coordinate.</summary>
        protected virtual int ReadTileCoordinate(long address)
        {
            return (int)(ReadU32(address) >> 16);
        }

        private PlayerFacingDirection ToFacingDirection(byte value)
        {
            // Up, Down, Left, Right
            var index = Pointers.FacingValues.IndexOf(value);
            switch (index)
            {
                case 0:
                    return PlayerFacingDirection.Up;
                case 2:
                    return PlayerFacingDirection.Left;
                case 3:
                    return PlayerFacingDirection.Right;
                default:
                    return PlayerFacingDirection.Down;
            }
        }

        public override int GetTID()
        {
            var baseAddress = GetBase();
            return baseAddress == null ? 0 : ReadU16(baseAddress.Value + Pointers.TrainerId);
        }

        /// <summary>Gen 3 script tasks (PokeFinder / debug window): none on DS / 3DS.</summary>
        public override ICollection<GTask> GetTasks()
        {
            return new List<GTask>();
        }

        /// <summary>
        /// Gen 3 fishing bot state: the DS / 3DS fishing bot reads its own bite flag through the runner
        /// (<see cref="GetFishingBite"/>), so this is never used there.
        /// </summary>
        public override FishingState GetFishingResult()
        {
            return FishingState.None;
        }

        public override int GetSID()
        {
            var baseAddress = GetBase();
            return baseAddress == null ? 0 : ReadU16(baseAddress.Value + Pointers.TrainerId + 2);
        }

        #endregion

        #region Pokemon

        public override int GetPartyCount()
        {
            var baseAddress = GetBase();
            if (baseAddress == null)
            {
                return 0;
            }

            return Math.Min((int)ReadU8(baseAddress.Value + Pointers.PartyCount), 6);
        }

        public override IReadOnlyList<Pokemon> GetParty()
        {
            var list = new List<Pokemon>();
            var baseAddress = GetBase();
            if (baseAddress == null)
            {
                return list;
            }

            var count = GetPartyCount();
            var partyAddress = baseAddress.Value + Pointers.Party;
            var bytes = ReadBytes(partyAddress, count * PartyPokemonSize);

            for (int i = 0; i < count; i++)
            {
                var data = new byte[PartyPokemonSize];
                Array.Copy(bytes, i * PartyPokemonSize, data, 0, PartyPokemonSize);
                var pokemon = TryParsePokemon(data, new MemoryLocation(partyAddress, i * PartyPokemonSize, PartyPokemonSize, Domain));
                if (pokemon != null)
                {
                    list.Add(pokemon);
                    _invalidReads[i] = 0;
                }
                else if (++_invalidReads[i] == InvalidReadsBeforeWarning)
                {
                    // Corrupted / badly injected Pokemon: logged once
                    Log.Warn($"Party slot {i + 1} skipped: invalid checksum");
                }
            }

            return list;
        }

        public override Pokemon GetOpponent()
        {
            return TryGetOpponent() ?? throw new InvalidOperationException(Messages.Pokemon_NotValid);
        }

        /// <summary>
        /// Returns the first wild/trainer Pokemon of the current battle, or null if its data isn't ready.
        /// </summary>
        public virtual Pokemon? TryGetOpponent()
        {
            var baseAddress = GetBase();
            if (baseAddress == null)
            {
                return null;
            }

            var foeBase = GetFoeBase(baseAddress.Value);
            if (foeBase == null)
            {
                return null;
            }

            var foeCount = ReadU8(foeBase.Value + Pointers.FoeCount);
            if (foeCount == 0 || foeCount > 6)
            {
                return null;
            }

            var address = foeBase.Value + Pointers.Foe;
            var data = ReadBytes(address, PartyPokemonSize);
            return TryParsePokemon(data, new MemoryLocation(address, 0, PartyPokemonSize, Domain));
        }

        /// <summary>
        /// Decrypts and parses a 136 bytes (box) or party size Pokemon (Gen 4: 236 bytes, Gen 5: 220 bytes, same
        /// encryption and blocks). Returns null if the checksum doesn't match.
        /// https://projectpokemon.org/home/docs/gen-4/pkm-structure-r65/ and /gen-5/bw-save-structure-r60/
        /// </summary>
        public virtual Pokemon? TryParsePokemon(byte[] encrypted, MemoryLocation memoryLocation)
        {
            var data = Decrypt(encrypted, out var checksum, out var calculatedChecksum);
            if (data == null)
            {
                return null;
            }

            var pid = BitConverter.ToUInt32(data, 0x00);

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
            var friendship = (int)data[0x14];
            var abilityId = data[0x15];
            var markings = (PokemonMark)(data[0x16] & 0xF);
            var language = (PokemonLanguageCode)data[0x17];
            var evs = new PokemonEVS(data[0x18], data[0x19], data[0x1A], data[0x1B], data[0x1C], data[0x1D]);
            var conditions = new PokemonCondition(data[0x1E], data[0x1F], data[0x20], data[0x21], data[0x22], data[0x23]);

            #endregion

            #region Block B

            var moves = new List<PokemonAttack>();
            for (int i = 0; i < 4; i++)
            {
                var moveId = BitConverter.ToUInt16(data, 0x28 + i * 2);
                if (moveId != 0)
                {
                    var move = GenerationInfo.Moves.FirstOrDefault(x => x.Id == moveId) ?? new PokemonMove(moveId, $"#{moveId}", "???", "???", 0, 0, 0);
                    moves.Add(new PokemonAttack(move, data[0x30 + i]));
                }
            }

            var ppBonuses = (int)BitConverter.ToUInt32(data, 0x34);
            var ivData = BitConverter.ToUInt32(data, 0x38);
            var ivHP = (int)(ivData & 0x1F);
            var ivAttack = (int)(ivData >> 5 & 0x1F);
            var ivDefense = (int)(ivData >> 10 & 0x1F);
            var ivSpeed = (int)(ivData >> 15 & 0x1F);
            var ivSpAttack = (int)(ivData >> 20 & 0x1F);
            var ivSpDefense = (int)(ivData >> 25 & 0x1F);
            var isEgg = (ivData >> 30 & 0x1) == 1;
            var ivs = new PokemonIVS(ivHP, ivAttack, ivDefense, ivSpeed, ivSpAttack, ivSpDefense);

            var genderFlags = data[0x40] >> 1 & 0x3;
            var gender = (genderFlags & 0x2) != 0 ? PokemonGender.Genderless : (genderFlags & 0x1) != 0 ? PokemonGender.Female : PokemonGender.Male;

            // Gen 5 stores the nature (Gen 4 derives it from the PID)
            var natureId = IsGen5 ? data[0x41] : (int)(pid % 25);

            // Platinum added extended locations in block B, DP (and Gen 5) only use the ones in block D
            var eggLocation = BitConverter.ToUInt16(data, 0x44);
            var metLocationId = BitConverter.ToUInt16(data, 0x46);

            #endregion

            #region Block C

            var nickname = DecodeString(data, 0x48, 11);
            var gameId = data[0x5F];

            #endregion

            #region Block D

            var otName = DecodeString(data, 0x68, 8);
            if (eggLocation == 0)
            {
                eggLocation = BitConverter.ToUInt16(data, 0x7E);
            }

            if (metLocationId == 0)
            {
                metLocationId = BitConverter.ToUInt16(data, 0x80);
            }

            var pokerus = new PokemonPokerus(data[0x82] & 0xF, data[0x82] >> 4);
            var pokeballId = data[0x83];
            var metLevel = data[0x84] & 0x7F;
            var otIsMale = (data[0x84] >> 7) == 0;

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

            var shinyValue = otId ^ otSecretId ^ (int)(pid >> 16) ^ (int)(pid & 0xFFFF);
            var shiny = shinyValue < 8;

            #endregion

            if (encrypted.Length >= PartyPokemonSize)
            {
                // Same party stats layout in Gen 4 and 5
                var status = (PokemonStatusType)data[0x88];
                var level = (int)data[0x8C];
                var currentHP = BitConverter.ToUInt16(data, 0x8E);
                var maxHP = BitConverter.ToUInt16(data, 0x90);
                var attack = BitConverter.ToUInt16(data, 0x92);
                var defense = BitConverter.ToUInt16(data, 0x94);
                var speed = BitConverter.ToUInt16(data, 0x96);
                var spAttack = BitConverter.ToUInt16(data, 0x98);
                var spDefense = BitConverter.ToUInt16(data, 0x9A);

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
        /// Returns the decrypted data with the blocks back in A/B/C/D order, or null if the checksum doesn't match.
        /// Blocks (0x08-0x87) are XORed with an LCRNG seeded by the checksum, the party stats (0x88-) with one seeded by the PID.
        /// </summary>
        public static byte[]? Decrypt(byte[] encrypted, out int checksum, out int calculatedChecksum)
        {
            checksum = BitConverter.ToUInt16(encrypted, 0x06);
            calculatedChecksum = 0;
            if (encrypted.Length < BoxPokemonSize)
            {
                return null;
            }

            var pid = BitConverter.ToUInt32(encrypted, 0x00);
            // Flags at 0x04: bit 0 = party stats decrypted, bit 1 = blocks decrypted (the game decrypts a Pokemon
            // in place while it changes it, a frame can end before it is encrypted again)
            var flags = BitConverter.ToUInt16(encrypted, 0x04);
            var decrypted = (byte[])encrypted.Clone();
            if ((flags & 0x2) == 0)
            {
                XorWithPrng(decrypted, 0x08, BoxPokemonSize, (uint)checksum);
            }

            var sum = 0;
            for (int i = 0x08; i < BoxPokemonSize; i += 2)
            {
                sum += BitConverter.ToUInt16(decrypted, i);
            }

            calculatedChecksum = sum & 0xFFFF;
            if (calculatedChecksum != checksum || pid == 0 && checksum == 0)
            {
                return null;
            }

            // Unshuffle the 4 blocks of 32 bytes
            var order = _blockOrders[((pid & 0x3E000) >> 0xD) % 24];
            var result = (byte[])decrypted.Clone();
            for (int i = 0; i < 4; i++)
            {
                var target = order[i] - 'A';
                Array.Copy(decrypted, 0x08 + i * 32, result, 0x08 + target * 32, 32);
            }

            if (encrypted.Length > BoxPokemonSize && (flags & 0x1) == 0)
            {
                XorWithPrng(result, BoxPokemonSize, encrypted.Length, pid);
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

        #region Text

        public string ReadString(long address, int maxLength)
        {
            return DecodeString(ReadBytes(address, maxLength * 2), 0, maxLength);
        }

        /// <summary>
        /// DS text is 16 bits per character, 0xFFFF (or 0) terminated: an index in the character table in Gen 4,
        /// UTF-16 in Gen 5 (with the gender symbols in the private range).
        /// </summary>
        public string DecodeString(byte[] data, int offset, int maxLength)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < maxLength && offset + i * 2 + 1 < data.Length; i++)
            {
                var code = BitConverter.ToUInt16(data, offset + i * 2);
                if (code == 0xFFFF || code == 0)
                {
                    break;
                }

                if (Utf16Text)
                {
                    sb.Append(code == 0x246D ? '♂' : code == 0x246E ? '♀' : (char)code);
                }
                else
                {
                    sb.Append(code < GenerationInfo.Characters.Count ? GenerationInfo.Characters[code] : "?");
                }
            }

            return sb.ToString();
        }

        #endregion

        #region RNG

        private static readonly Random _random = new Random();

        public override uint GetCurrentSeed()
        {
            if (Pointers.Rng == 0)
            {
                // 3DS: no LCRNG, the MT index tells where the game is
                return Pointers.MtIndex == 0 ? 0 : ReadU32(Pointers.MtIndex);
            }

            // Gen 5: upper half of the 64 bits state (the part the game uses)
            return Pointers.RngSize == 8 ? ReadU32(Pointers.Rng + 4) : ReadU32(Pointers.Rng);
        }

        /// <summary>
        /// Writes a random LCRNG state. The DS games only advance it when they draw numbers, so after loading a
        /// save state every starter / gift / static Pokemon would be the same without this (verified in Platinum:
        /// same seed = same PID, different seeds = different PIDs).
        /// </summary>
        public override uint RandomizeCurrentSeed()
        {
            var seed = RandomU32();
            if (Pointers.Rng != 0)
            {
                WriteU32(Pointers.Rng, seed);
                if (Pointers.RngSize == 8)
                {
                    seed = RandomU32();
                    WriteU32(Pointers.Rng + 4, seed);
                }
            }

            var mtSeed = RandomizeMt();
            return Pointers.Rng != 0 ? seed : mtSeed;
        }

        /// <summary>
        /// Seeds the Mersenne Twister again with a random seed, like a boot with another seed would: the state is
        /// re-initialized and the index set to 624, so the next number comes from a fresh twist.
        /// The Day-Care egg PID is the MT's next number (verified in Platinum); Gen 5 takes the IVs from it.
        /// </summary>
        public uint RandomizeMt()
        {
            if (Pointers.Mt == 0 || Pointers.MtIndex == 0)
            {
                return 0;
            }

            var state = new byte[624 * 4];
            var value = RandomU32();
            for (int i = 0; i < 624; i++)
            {
                if (i > 0)
                {
                    value = unchecked(1812433253u * (value ^ (value >> 30)) + (uint)i);
                }

                state[i * 4] = (byte)value;
                state[i * 4 + 1] = (byte)(value >> 8);
                state[i * 4 + 2] = (byte)(value >> 16);
                state[i * 4 + 3] = (byte)(value >> 24);
            }

            APIContainer.Memory.WriteByteRange(Pointers.Mt, state, Domain);
            WriteU32(Pointers.MtIndex, (uint)Pointers.MtSeedIndex);
            return BitConverter.ToUInt32(state, 0);
        }

        private static uint RandomU32()
        {
            var bytes = new byte[4];
            lock (_random)
            {
                _random.NextBytes(bytes);
            }

            return BitConverter.ToUInt32(bytes, 0);
        }

        #endregion

        #region Day-Care

        /// <summary>Pokemon left at the Day-Care (0 to 2).</summary>
        public IReadOnlyList<Pokemon> GetDaycare()
        {
            var list = new List<Pokemon>();
            var baseAddress = GetBase();
            if (baseAddress == null || Pointers.Daycare == 0)
            {
                return list;
            }

            for (int i = 0; i < 2; i++)
            {
                var slot = baseAddress.Value + Pointers.Daycare + i * Pointers.DaycareSlotSize;
                // Gen 5: a free slot can still hold the data of a Pokemon taken back, the flag before it tells
                if (Pointers.DaycarePokemonOffset != 0 && ReadU32(slot) == 0)
                {
                    continue;
                }

                var address = slot + Pointers.DaycarePokemonOffset;
                var pokemon = TryParsePokemon(ReadBytes(address, StoredPokemonSize), new MemoryLocation(address, 0, StoredPokemonSize, Domain));
                if (pokemon != null)
                {
                    list.Add(pokemon);
                }
            }

            return list;
        }

        /// <summary>
        /// Gen 4: PID of the egg the Day-Care Man has, 0 if he has none.
        /// Gen 5: non-zero while he has an egg (its PID is only decided when the egg is taken).
        /// </summary>
        public uint GetDaycareOffspring()
        {
            var baseAddress = GetBase();
            return baseAddress == null || Pointers.DaycareOffspring == 0 ? 0 : ReadU32(baseAddress.Value + Pointers.DaycareOffspring);
        }

        /// <summary>
        /// Steps counted by the second Day-Care slot: the game tries to make an egg each time its low byte reaches 255
        /// (verified in Platinum, like Gen 3).
        /// </summary>
        public uint GetDaycareSteps()
        {
            var baseAddress = GetBase();
            if (baseAddress == null || Pointers.Daycare == 0)
            {
                return 0;
            }

            return ReadU32(baseAddress.Value + Pointers.Daycare + 2 * Pointers.DaycareSlotSize - 4);
        }

        /// <summary>
        /// B2W2: writes a random egg seed in the Day-Care (no-op for the other games), as if the waiting egg had been
        /// made at another time.
        /// </summary>
        public void RandomizeDaycareEggSeed()
        {
            var baseAddress = GetBase();
            if (baseAddress == null || Pointers.DaycareEggSeed == 0)
            {
                return;
            }

            var address = baseAddress.Value + Pointers.DaycareEggSeed;
            WriteU32(address, RandomU32());
            WriteU32(address + 4, RandomU32());
        }

        /// <summary>Shiny for the player (the Day-Care egg gets the player as trainer when it is taken).</summary>
        public bool IsShinyForPlayer(uint pid)
        {
            return (GetTID() ^ GetSID() ^ (int)(pid >> 16) ^ (int)(pid & 0xFFFF)) < 8;
        }

        #endregion

        #region Field

        /// <summary>True when the registered items can be read (not known for B2W2).</summary>
        public bool CanReadRegisteredItems => Pointers.RegisteredItem != 0 || Pointers.RegisteredSlots != 0;

        /// <summary>
        /// Item ids registered on Y, without the empty slots: one slot in DPPt, two in HGSS, a list in BW (Y opens a
        /// choice when more than one item is registered). An unknown BW index is returned as a negative value.
        /// </summary>
        public IReadOnlyList<int> GetRegisteredItems()
        {
            var list = new List<int>();
            var baseAddress = GetBase();
            if (baseAddress == null)
            {
                return list;
            }

            if (Pointers.RegisteredSlots != 0)
            {
                var slots = ReadBytes(baseAddress.Value + Pointers.RegisteredSlots, Pointers.RegisteredSlotCount);
                foreach (var slot in slots)
                {
                    if (slot == Pointers.RegisteredSlotEmpty)
                    {
                        continue;
                    }

                    var id = slot < Pointers.RegisteredSlotItems.Count ? Pointers.RegisteredSlotItems[slot] : 0;
                    list.Add(id != 0 ? id : -slot - 1);
                }

                return list;
            }

            foreach (var offset in new[] { Pointers.RegisteredItem, Pointers.RegisteredItem2 })
            {
                if (offset == 0)
                {
                    continue;
                }

                var id = ReadU16(baseAddress.Value + offset);
                if (id != 0)
                {
                    list.Add(id);
                }
            }

            return list;
        }

        /// <summary>Item ids of the key items pocket.</summary>
        public IReadOnlyList<int> GetKeyItems()
        {
            var list = new List<int>();
            var baseAddress = GetBase();
            if (baseAddress == null)
            {
                return list;
            }

            var bytes = ReadBytes(baseAddress.Value + Pointers.KeyItems, 50 * 4);
            for (int i = 0; i < 50; i++)
            {
                var id = BitConverter.ToUInt16(bytes, i * 4);
                if (id != 0)
                {
                    list.Add(id);
                }
            }

            return list;
        }

        /// <summary>0 at rest, 1 when something bites, another value when nothing bites (only meaningful while fishing).</summary>
        public int GetFishingBite()
        {
            if (Pointers.FishingNoBite != 0)
            {
                // Gen 5: u16 bite value, separate "nothing" flag
                if (ReadU16(Pointers.FishingBite) == Pointers.FishingBiteValue)
                {
                    return 1;
                }

                return ReadU8(Pointers.FishingNoBite) != 0 ? 2 : 0;
            }

            if (Pointers.FishingBiteAnchor != 0)
            {
                // Omega Ruby/Alpha Sapphire: no bite object (0) while waiting and when nothing bites
                long bite = ReadU32(Pointers.FishingBiteAnchor);
                return IsDataPointer(bite, Pointers.FishingBite) ? ReadU8(bite + Pointers.FishingBite) : 0;
            }

            return ReadU8(Pointers.FishingBite);
        }

        /// <summary>
        /// Cursor of the starter briefcase / gift box (0-2), -1 if the choice isn't shown, -2 if it is shown without
        /// any ball highlighted (Gen 5 and Omega Ruby/Alpha Sapphire before the first direction press).
        /// </summary>
        public int GetStarterSelection()
        {
            var baseAddress = GetBase();
            if (baseAddress == null)
            {
                return -1;
            }

            var mask = (int)Pointers.StartersReadyMask;
            var ready = ReadU8(baseAddress.Value + Pointers.StartersReady) & 0xF & mask;
            if (ready != mask)
            {
                return -1;
            }

            var selected = ReadU8(baseAddress.Value + Pointers.StarterSelected);
            return selected <= 2 ? selected : -2;
        }

        /// <summary>
        /// HGSS: the three starters of Elm's lab, generated together when the choice scene starts. Empty until they are
        /// all ready (and always empty for the other games, see <see cref="NdsPointers.StarterData"/>).
        /// </summary>
        public virtual IReadOnlyList<Pokemon> GetGeneratedStarters()
        {
            var list = new List<Pokemon>();
            var baseAddress = GetBase();
            if (baseAddress == null || Pointers.StarterData == 0)
            {
                return list;
            }

            var address = baseAddress.Value + Pointers.StarterData;
            if (ReadU32(address - 8) != 0 || ReadU32(address - 4) == 0)
            {
                return list;
            }

            var bytes = ReadBytes(address, 3 * PartyPokemonSize);
            for (int i = 0; i < 3; i++)
            {
                var data = new byte[PartyPokemonSize];
                Array.Copy(bytes, i * PartyPokemonSize, data, 0, PartyPokemonSize);
                var pokemon = TryParsePokemon(data, new MemoryLocation(address, i * PartyPokemonSize, PartyPokemonSize, Domain));
                if (pokemon == null)
                {
                    return new List<Pokemon>();
                }

                list.Add(pokemon);
            }

            return list;
        }

        public bool IsOnBike()
        {
            var baseAddress = GetBase();
            return baseAddress != null && Pointers.Bike != 0 && ReadU8(baseAddress.Value + Pointers.Bike) == 1;
        }

        /// <summary>The bike has gears (DPPt only, the HGSS and Gen 5 bikes have none).</summary>
        public bool HasBikeGears => Pointers.BikeGear != 0;

        public bool IsBikeFastGear()
        {
            var baseAddress = GetBase();
            return baseAddress != null && (!HasBikeGears || ReadU8(baseAddress.Value + Pointers.BikeGear) == 1);
        }

        /// <summary>
        /// Player coordinates as stored (16.16 fixed point): they move every 2 frames while walking,
        /// so they tell if the player is moving, unlike the tile position.
        /// </summary>
        public (uint X, uint Z) GetRawPosition()
        {
            return (ReadU32(Pointers.PlayerX), ReadU32(Pointers.PlayerZ));
        }

        #endregion
    }
}
