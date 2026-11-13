using BizHawk.Client.Common;
using Pokebot.Exceptions;
using Pokebot.Models.Config;
using Pokebot.Models.Player;
using Pokebot.Models.Pokemons;
using Pokebot.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Pokebot.Models.Memory
{
    /// <summary>
    /// Memory reader for Pokémon Red / Blue / Yellow.
    /// Layouts come from pret/pokered and pret/pokeyellow (macros/ram.asm: party_struct and battle_struct).
    /// </summary>
    internal class Gen1Memory : CommonGenMemory
    {
        private const int ConfirmedOpponentReadsRequired = 2;
        private const int PartyLength = 6;
        private const byte NameTerminator = 0x50;
        private const byte PPMask = 0x3F;

        // party_struct offsets (wPartyMon1, 0x2C bytes)
        private const int PartySpeciesOffset = 0x00;
        private const int PartyHPOffset = 0x01;
        private const int PartyStatusOffset = 0x04;
        private const int PartyType1Offset = 0x05;
        private const int PartyType2Offset = 0x06;
        private const int PartyMovesOffset = 0x08;
        private const int PartyOTIDOffset = 0x0C;
        private const int PartyExpOffset = 0x0E;
        private const int PartyHPExpOffset = 0x11;
        private const int PartyDVsOffset = 0x1B;
        private const int PartyPPOffset = 0x1D;
        private const int PartyLevelOffset = 0x21;
        private const int PartyMaxHPOffset = 0x22;

        // battle_struct offsets (wEnemyMon, 0x1D bytes)
        private const int BattleSpeciesOffset = 0x00;
        private const int BattleHPOffset = 0x01;
        private const int BattleStatusOffset = 0x04;
        private const int BattleType1Offset = 0x05;
        private const int BattleType2Offset = 0x06;
        private const int BattleMovesOffset = 0x08;
        private const int BattleDVsOffset = 0x0C;
        private const int BattleLevelOffset = 0x0E;
        private const int BattleMaxHPOffset = 0x0F;
        private const int BattlePPOffset = 0x19;
        private const int BattleStructSize = 0x1D;

        // wIsInBattle values (0 = none, $FF = lost battle)
        private const byte WildBattle = 1;
        private const byte TrainerBattle = 2;

        // Battle main menu, see DisplayBattleMenu in engine/battle/core.asm
        private const byte BattleMenuTopItemY = 0x0E;
        private const byte BattleMenuLeftColumnX = 0x09;
        private const byte BattleMenuRightColumnX = 0x0F;
        private const byte SafariMenuLeftColumnX = 0x01;
        private const byte SafariMenuRightColumnX = 0x0D;
        private const byte BattleMenuRunItem = 0x03; // right column (+2), bottom row (1)

        // SPRITE_FACING_* values stored in wSpritePlayerStateData1FacingDirection
        private const byte SpriteFacingDown = 0x00;
        private const byte SpriteFacingUp = 0x04;
        private const byte SpriteFacingLeft = 0x08;
        private const byte SpriteFacingRight = 0x0C;

        private string? _pendingOpponentSignature;
        private int _pendingOpponentReads;
        private int _pendingOpponentFrame;

        public Gen1Memory(ApiContainer apiContainer, VersionInfo versionInfo, HashData hashData, GenerationInfo generationInfo) :
            base(apiContainer, versionInfo, hashData, generationInfo)
        { }

        private Symbol GetRequiredSymbol(string name)
        {
            var symbol = GetSymbol(name);
            if (symbol == null)
            {
                throw new PokebotException($"Symbol '{name}' is missing from '{HashData.Symbols.Main}' symbols.");
            }

            return symbol;
        }

        private byte[] ReadSymbol(string name)
        {
            return SymbolUtil.Read(APIContainer, GetRequiredSymbol(name)).ToArray();
        }

        public override void TrySetEscape()
        {
            // The battle menu column is code flow state, so the cursor can't be moved while the menu is open.
            // Instead, preselect RUN for the next time the battle menu is displayed (reset to FIGHT at battle start).
            SymbolUtil.Write(APIContainer, GetRequiredSymbol("wBattleAndStartSavedMenuItem"), new byte[] { BattleMenuRunItem });
        }

        /// <summary>
        /// Returns a <see cref="BattleActionSelectionCursor"/> value, or -1 when the battle main menu isn't the active menu.
        /// </summary>
        public override int GetActionSelectionCursor()
        {
            var topMenuItemY = ReadSymbol("wTopMenuItemY")[0];
            var topMenuItemX = ReadSymbol("wTopMenuItemX")[0];
            var currentMenuItem = ReadSymbol("wCurrentMenuItem")[0];

            // After A is pressed in the right column, the game adds 2 to wCurrentMenuItem, so only 0 and 1 are live rows.
            if (topMenuItemY != BattleMenuTopItemY || currentMenuItem > 1)
            {
                return -1;
            }

            var isTopRow = currentMenuItem == 0;
            if (topMenuItemX == BattleMenuLeftColumnX || topMenuItemX == SafariMenuLeftColumnX)
            {
                return isTopRow ? (int)BattleActionSelectionCursor.Moves : (int)BattleActionSelectionCursor.Bag;
            }

            if (topMenuItemX == BattleMenuRightColumnX || topMenuItemX == SafariMenuRightColumnX)
            {
                return isTopRow ? (int)BattleActionSelectionCursor.Team : (int)BattleActionSelectionCursor.Escape;
            }

            return -1;
        }

        public override GameState GetGameState()
        {
            var battleType = ReadSymbol("wIsInBattle")[0];
            if (battleType == WildBattle || battleType == TrainerBattle)
            {
                if (!TryGetStableOpponent(out _, out var signature))
                {
                    return GameState.StartBattle;
                }

                var frame = APIContainer.Emulation.FrameCount();
                if (_pendingOpponentSignature != signature)
                {
                    _pendingOpponentSignature = signature;
                    _pendingOpponentReads = 1;
                    _pendingOpponentFrame = frame;
                    return GameState.StartBattle;
                }

                // Only count one confirmation per emulated frame, data must be stable across frames
                if (_pendingOpponentFrame != frame)
                {
                    _pendingOpponentFrame = frame;
                    _pendingOpponentReads++;
                }

                if (_pendingOpponentReads < ConfirmedOpponentReadsRequired)
                {
                    return GameState.StartBattle;
                }

                return GameState.Battle;
            }

            ResetBattleState();

            // WRAM is cleared on boot/reset, the player sprite and name are only set once the game world is loaded
            var playerPictureId = ReadSymbol("wSpritePlayerStateData1PictureID")[0];
            var playerName = ReadSymbol("wPlayerName");
            if (playerPictureId != 0 && playerName[0] != 0x00 && playerName[0] != NameTerminator)
            {
                return GameState.Overworld;
            }

            return GameState.TitleScreen;
        }

        public override Pokemon GetOpponent()
        {
            if (!TryGetStableOpponent(out var opponent, out _))
            {
                throw new InvalidOperationException("Opponent data is not ready in memory.");
            }

            return opponent;
        }

        public override IReadOnlyList<Pokemon> GetParty()
        {
            var list = new List<Pokemon>();
            var count = Math.Min(GetPartyCount(), PartyLength);
            for (int i = 0; i < count; i++)
            {
                var pokemon = GetPartyPokemon(i);
                if (pokemon == null)
                {
                    break;
                }

                list.Add(pokemon);
            }

            return list;
        }

        private Pokemon? GetPartyPokemon(int index)
        {
            var wPartyMon1 = GetRequiredSymbol("wPartyMon1");
            var wPartyMon1Nick = GetRequiredSymbol("wPartyMon1Nick");
            var wPartyMon1OT = GetRequiredSymbol("wPartyMon1OT");
            var size = wPartyMon1.Size;
            var offset = index * size;
            var bytesPokemon = SymbolUtil.Read(APIContainer, wPartyMon1.Address, offset, size).ToArray();
            var bytesNickname = SymbolUtil.Read(APIContainer, wPartyMon1Nick.Address, index * wPartyMon1Nick.Size, wPartyMon1Nick.Size)
                .TakeWhile(x => x != NameTerminator)
                .ToArray();
            var bytesOT = SymbolUtil.Read(APIContainer, wPartyMon1OT.Address, index * wPartyMon1OT.Size, wPartyMon1OT.Size)
                .TakeWhile(x => x != NameTerminator)
                .ToArray();
            return ParsePartyPokemon(bytesPokemon, bytesNickname, bytesOT, new MemoryLocation(wPartyMon1.Address, offset, size));
        }

        private PokemonConfig GetSpecies(int speciesId)
        {
            var species = GenerationInfo.Pokemons.FirstOrDefault(x => x.Id == speciesId);
            if (species == null)
            {
                throw new InvalidOperationException($"Species not found for internal id 0x{speciesId:X2}");
            }

            return species;
        }

        /// <summary>
        /// Gen 1 stores types in each Pokémon structure, ids are the Gen 1 type constants (Fire = 0x14...).
        /// </summary>
        private List<PokemonType> GetTypes(byte type1, byte type2)
        {
            var types = new List<PokemonType>();
            foreach (var typeId in new[] { type1, type2 }.Distinct())
            {
                var type = GenerationInfo.Types.FirstOrDefault(x => x.Id == typeId);
                if (type != null)
                {
                    types.Add(type);
                }
            }

            return types;
        }

        private static PokemonStatusType GetStatus(byte status)
        {
            // bits 0-2: sleep counter, bit 3: PSN, bit 4: BRN, bit 5: FRZ, bit 6: PAR
            if ((status & 0x07) != 0)
            {
                return PokemonStatusType.Sleep;
            }

            if ((status & 0x08) != 0)
            {
                return PokemonStatusType.Poison;
            }

            if ((status & 0x10) != 0)
            {
                return PokemonStatusType.Burn;
            }

            if ((status & 0x20) != 0)
            {
                return PokemonStatusType.Freeze;
            }

            if ((status & 0x40) != 0)
            {
                return PokemonStatusType.Paralysis;
            }

            return PokemonStatusType.None;
        }

        private static PokemonIVS GetIVs(byte atkDefDVs, byte spdSpcDVs)
        {
            var attackIV = atkDefDVs >> 4;
            var defenseIV = atkDefDVs & 0x0F;
            var speedIV = spdSpcDVs >> 4;
            var specialIV = spdSpcDVs & 0x0F;
            var hpIV = ((attackIV % 2) * 8) + ((defenseIV % 2) * 4) + ((speedIV % 2) * 2) + (specialIV % 2);
            return new PokemonIVS(hpIV, attackIV, defenseIV, speedIV, specialIV, specialIV);
        }

        /// <summary>
        /// Gen 1 has no shiny Pokémon, this is the Gen 2 rule applied when the Pokémon is traded/transferred.
        /// </summary>
        private static bool IsShiny(PokemonIVS ivs)
        {
            var attackIV = ivs.Attack;
            return ivs.Defense == 0xA && ivs.Speed == 0xA && ivs.SpAttack == 0xA &&
               (attackIV == 0x2 || attackIV == 0x3 || attackIV == 0x6 || attackIV == 0x7 ||
                attackIV == 0xA || attackIV == 0xB || attackIV == 0xE || attackIV == 0xF);
        }

        /// <summary>
        /// Gen 1 has no PID, build a stable identifier from the species and the DVs.
        /// </summary>
        private static uint BuildPID(int speciesId, byte atkDefDVs, byte spdSpcDVs)
        {
            return ((uint)speciesId << 16) | ((uint)atkDefDVs << 8) | spdSpcDVs;
        }

        private List<PokemonAttack> GetMoves(byte[] bytesPokemon, int movesOffset, int ppOffset)
        {
            var moves = new List<PokemonAttack>();
            for (int i = 0; i < 4; i++)
            {
                var move = GenerationInfo.Moves.FirstOrDefault(x => x.Id == bytesPokemon[movesOffset + i]);
                if (move != null)
                {
                    // The 2 upper bits are the number of PP Up used
                    moves.Add(new PokemonAttack(move, bytesPokemon[ppOffset + i] & PPMask));
                }
            }

            return moves;
        }

        private static int GetPPBonuses(byte[] bytesPokemon, int ppOffset)
        {
            // Same packing as Gen 3: 2 bits per move
            var bonuses = 0;
            for (int i = 0; i < 4; i++)
            {
                bonuses |= (bytesPokemon[ppOffset + i] >> 6) << (i * 2);
            }

            return bonuses;
        }

        private Pokemon ParseOpponentPokemon(byte[] bytesPokemon, byte[] bytesNickname, MemoryLocation memoryLocation)
        {
            var speciesId = bytesPokemon[BattleSpeciesOffset];
            var species = GetSpecies(speciesId);

            var atkDefDVs = bytesPokemon[BattleDVsOffset];
            var spdSpcDVs = bytesPokemon[BattleDVsOffset + 1];
            var ivs = GetIVs(atkDefDVs, spdSpcDVs);
            var level = bytesPokemon[BattleLevelOffset];
            var currentHp = bytesPokemon.Skip(BattleHPOffset).Take(2).ToBE16();
            var maxHp = bytesPokemon.Skip(BattleMaxHPOffset).Take(2).ToBE16();
            var attack = bytesPokemon.Skip(BattleMaxHPOffset + 2).Take(2).ToBE16();
            var defense = bytesPokemon.Skip(BattleMaxHPOffset + 4).Take(2).ToBE16();
            var speed = bytesPokemon.Skip(BattleMaxHPOffset + 6).Take(2).ToBE16();
            var special = bytesPokemon.Skip(BattleMaxHPOffset + 8).Take(2).ToBE16();
            var types = GetTypes(bytesPokemon[BattleType1Offset], bytesPokemon[BattleType2Offset]);

            return new PartyPokemon(
                BuildPID(speciesId, atkDefDVs, spdSpcDVs),
                null,
                GetBytesText(bytesNickname),
                PokemonLanguageCode.Unknow,
                PokemonEggNameType.None,
                PokemonMark.None,
                0,
                0,
                speciesId,
                null,
                0,
                GetPPBonuses(bytesPokemon, BattlePPOffset),
                0,
                GetMoves(bytesPokemon, BattleMovesOffset, BattlePPOffset),
                new PokemonEVS(0, 0, 0, 0, 0, 0),
                null,
                null,
                null,
                level,
                null,
                ivs,
                false,
                null,
                species.Name,
                species.DexId,
                IsShiny(ivs),
                null,
                types,
                null,
                null,
                0,
                PokemonGender.Unknow,
                GetStatus(bytesPokemon[BattleStatusOffset]),
                level,
                false,
                currentHp,
                maxHp,
                attack,
                defense,
                speed,
                special,
                special,
                memoryLocation
            );
        }

        private Pokemon? ParsePartyPokemon(byte[] bytesPokemon, byte[] bytesNickname, byte[] bytesOT, MemoryLocation memoryLocation)
        {
            var speciesId = bytesPokemon[PartySpeciesOffset];
            if (speciesId == 0)
            {
                return null;
            }

            var species = GetSpecies(speciesId);

            var otid = bytesPokemon.Skip(PartyOTIDOffset).Take(2).ToBE16();
            var experience = bytesPokemon.Skip(PartyExpOffset).Take(3).toBE24();
            // Stat experience: HP, Attack, Defense, Speed, Special
            var hpEV = bytesPokemon.Skip(PartyHPExpOffset).Take(2).ToBE16();
            var attackEV = bytesPokemon.Skip(PartyHPExpOffset + 2).Take(2).ToBE16();
            var defenseEV = bytesPokemon.Skip(PartyHPExpOffset + 4).Take(2).ToBE16();
            var speedEV = bytesPokemon.Skip(PartyHPExpOffset + 6).Take(2).ToBE16();
            var specialEV = bytesPokemon.Skip(PartyHPExpOffset + 8).Take(2).ToBE16();
            var evs = new PokemonEVS(hpEV, attackEV, defenseEV, speedEV, specialEV, specialEV);

            var atkDefDVs = bytesPokemon[PartyDVsOffset];
            var spdSpcDVs = bytesPokemon[PartyDVsOffset + 1];
            var ivs = GetIVs(atkDefDVs, spdSpcDVs);
            var level = bytesPokemon[PartyLevelOffset];
            var currentHp = bytesPokemon.Skip(PartyHPOffset).Take(2).ToBE16();
            var maxHp = bytesPokemon.Skip(PartyMaxHPOffset).Take(2).ToBE16();
            var attack = bytesPokemon.Skip(PartyMaxHPOffset + 2).Take(2).ToBE16();
            var defense = bytesPokemon.Skip(PartyMaxHPOffset + 4).Take(2).ToBE16();
            var speed = bytesPokemon.Skip(PartyMaxHPOffset + 6).Take(2).ToBE16();
            var special = bytesPokemon.Skip(PartyMaxHPOffset + 8).Take(2).ToBE16();
            var types = GetTypes(bytesPokemon[PartyType1Offset], bytesPokemon[PartyType2Offset]);
            // Same rule as Gen 2: nature given on transfer to Gen 7 is experience % 25
            var nature = GenerationInfo.Natures[experience % GenerationInfo.Natures.Count];

            return new PartyPokemon(
                BuildPID(speciesId, atkDefDVs, spdSpcDVs),
                new PokemonOriginalTrainer(otid, 0, GetBytesText(bytesOT)),
                GetBytesText(bytesNickname),
                PokemonLanguageCode.Unknow,
                PokemonEggNameType.None,
                PokemonMark.None,
                0,
                0,
                speciesId,
                null,
                (uint)experience,
                GetPPBonuses(bytesPokemon, PartyPPOffset),
                0,
                GetMoves(bytesPokemon, PartyMovesOffset, PartyPPOffset),
                evs,
                null,
                null,
                null,
                level,
                null,
                ivs,
                false,
                null,
                species.Name,
                species.DexId,
                IsShiny(ivs),
                nature,
                types,
                null,
                null,
                0,
                PokemonGender.Unknow,
                GetStatus(bytesPokemon[PartyStatusOffset]),
                level,
                false,
                currentHp,
                maxHp,
                attack,
                defense,
                speed,
                special,
                special,
                memoryLocation
            );
        }

        private bool TryGetStableOpponent(out Pokemon opponent, out string signature)
        {
            opponent = null!;
            signature = string.Empty;

            var wEnemyMon = GetRequiredSymbol("wEnemyMon");
            var bytesPokemon = ReadSymbol("wEnemyMon");
            var bytesNickname = ReadSymbol("wEnemyMonNick").TakeWhile(x => x != NameTerminator).ToArray();

            if (!IsOpponentDataValid(bytesPokemon, bytesNickname))
            {
                return false;
            }

            signature = BuildOpponentSignature(bytesPokemon, bytesNickname);
            opponent = ParseOpponentPokemon(bytesPokemon, bytesNickname, new MemoryLocation(wEnemyMon.Address, 0, wEnemyMon.Size));
            return true;
        }

        private bool IsOpponentDataValid(byte[] bytesPokemon, byte[] bytesNickname)
        {
            if (bytesPokemon.Length < BattleStructSize || bytesNickname.Length == 0)
            {
                return false;
            }

            var speciesId = bytesPokemon[BattleSpeciesOffset];
            if (!GenerationInfo.Pokemons.Any(x => x.Id == speciesId))
            {
                return false;
            }

            var level = bytesPokemon[BattleLevelOffset];
            if (level == 0 || level > 100)
            {
                return false;
            }

            var currentHp = bytesPokemon.Skip(BattleHPOffset).Take(2).ToBE16();
            var maxHp = bytesPokemon.Skip(BattleMaxHPOffset).Take(2).ToBE16();
            if (maxHp <= 0 || currentHp > maxHp)
            {
                return false;
            }

            for (int i = 1; i <= 4; i++)
            {
                var stat = bytesPokemon.Skip(BattleMaxHPOffset + (i * 2)).Take(2).ToBE16();
                if (stat <= 0)
                {
                    return false;
                }
            }

            for (int i = 0; i < 4; i++)
            {
                var moveId = bytesPokemon[BattleMovesOffset + i];
                if (moveId != 0 && GenerationInfo.Moves.Any(x => x.Id == moveId))
                {
                    return true;
                }
            }

            return false;
        }

        private string BuildOpponentSignature(byte[] bytesPokemon, byte[] nickname)
        {
            var speciesId = bytesPokemon[BattleSpeciesOffset];
            var level = bytesPokemon[BattleLevelOffset];
            var currentHp = bytesPokemon.Skip(BattleHPOffset).Take(2).ToBE16();
            var maxHp = bytesPokemon.Skip(BattleMaxHPOffset).Take(2).ToBE16();
            return $"{speciesId:X2}-{level:D3}-{currentHp:D5}-{maxHp:D5}-{GetBytesText(nickname)}";
        }

        private void ResetBattleState()
        {
            _pendingOpponentSignature = null;
            _pendingOpponentReads = 0;
            _pendingOpponentFrame = 0;
        }

        /// <summary>
        /// Number of Pokémon fully added to the party.
        /// _AddPartyMon (engine/pokemon/add_mon.asm) increments wPartyCount and writes wPartySpecies,
        /// asks for a nickname, and only then generates the DVs and writes the party_struct.
        /// A Pokémon waiting on that nickname prompt is not counted yet.
        /// </summary>
        public override int GetPartyCount()
        {
            var count = GetRawPartyCount();
            if (count > 0 && !IsPartySlotWritten(count - 1))
            {
                return count - 1;
            }

            return count;
        }

        /// <summary>
        /// True while the last party Pokémon is being added (nickname prompt / naming screen),
        /// its DVs don't exist yet.
        /// </summary>
        public bool IsAddingPartyPokemon()
        {
            var count = GetRawPartyCount();
            return count > 0 && !IsPartySlotWritten(count - 1);
        }

        private int GetRawPartyCount()
        {
            return Math.Min((int)ReadSymbol("wPartyCount")[0], PartyLength);
        }

        private bool IsPartySlotWritten(int index)
        {
            var wPartySpecies = GetRequiredSymbol("wPartySpecies");
            var wPartyMon1 = GetRequiredSymbol("wPartyMon1");
            var listSpecies = SymbolUtil.Read(APIContainer, wPartySpecies.Address, index, 1)[0];
            var bytesPokemon = SymbolUtil.Read(APIContainer, wPartyMon1.Address, index * wPartyMon1.Size, wPartyMon1.Size);

            // The slot still holds old data (zeros, or a Pokémon that was deposited) until the struct is written.
            // Stale data of the same species can't be told apart, this is accepted as a rare edge case.
            var structSpecies = bytesPokemon[PartySpeciesOffset];
            return structSpecies != 0 && structSpecies == listSpecies && bytesPokemon[PartyLevelOffset] != 0;
        }

        public override PlayerData GetPlayer()
        {
            var x = ReadSymbol("wXCoord")[0];
            var y = ReadSymbol("wYCoord")[0];
            var walkCounter = ReadSymbol("wWalkCounter")[0];
            var facing = ReadSymbol("wSpritePlayerStateData1FacingDirection")[0];
            var map = ReadSymbol("wCurMap")[0];
            var playerId = ReadSymbol("wPlayerID").ToBE16();
            var playerName = GetBytesText(ReadSymbol("wPlayerName").TakeWhile(b => b != NameTerminator).ToArray());

            // wWalkCounter counts down during a step, coordinates are updated when it reaches 0
            var moving = walkCounter != 0;
            PlayerFacingDirection playerFacingDirection = PlayerFacingDirection.Down;
            switch (facing)
            {
                case SpriteFacingDown:
                    playerFacingDirection = PlayerFacingDirection.Down;
                    break;
                case SpriteFacingUp:
                    playerFacingDirection = PlayerFacingDirection.Up;
                    break;
                case SpriteFacingLeft:
                    playerFacingDirection = PlayerFacingDirection.Left;
                    break;
                case SpriteFacingRight:
                    playerFacingDirection = PlayerFacingDirection.Right;
                    break;
            }

            var position = new Position(x, y);

            // Gen 1 has no map groups and doesn't keep the previous coordinates
            return new PlayerData(
                position,
                position,
                moving ? PlayerRunningState.Moving : PlayerRunningState.NotMoving,
                moving ? TileTransitionState.KeepMoving : TileTransitionState.NotMoving,
                true,
                playerFacingDirection,
                0,
                map,
                playerId,
                playerName
            );
        }

        public override int GetSID()
        {
            return 0;
        }

        public override ICollection<GTask> GetTasks()
        {
            return new List<GTask>();
        }

        public override int GetTID()
        {
            return ReadSymbol("wPlayerID").ToBE16();
        }

        public override uint GetCurrentSeed()
        {
            var addSeed = ReadSymbol("hRandomAdd")[0];
            var subSeed = ReadSymbol("hRandomSub")[0];

            return ((uint)subSeed << 8) | addSeed;
        }

        public override uint RandomizeCurrentSeed()
        {
            var rng = new Random();
            uint newSeed = (uint)rng.Next(0, 0x10000);

            byte addSeed = (byte)(newSeed & 0xFF);
            byte subSeed = (byte)((newSeed >> 8) & 0xFF);
            SymbolUtil.Write(APIContainer, GetRequiredSymbol("hRandomAdd"), new byte[] { addSeed });
            SymbolUtil.Write(APIContainer, GetRequiredSymbol("hRandomSub"), new byte[] { subSeed });

            return ((uint)subSeed << 8) | addSeed;
        }

        public override FishingState GetFishingResult()
        {
            return FishingState.None;
        }

        public override bool CanSetShiny()
        {
            return true;
        }

        public override Pokemon SetShiny(Pokemon pokemon)
        {
            // Gen 2 shiny DVs (Attack 15, Defense/Speed/Special 10)
            byte atkDef = 0xFA;
            byte spdSpc = 0xAA;

            var wEnemyMon = GetRequiredSymbol("wEnemyMon");
            var opponentPokemon = pokemon.MemoryLocation.Address == wEnemyMon.Address;

            long dvAddr = pokemon.MemoryLocation.Address + pokemon.MemoryLocation.Offset + (opponentPokemon ? BattleDVsOffset : PartyDVsOffset);
            SymbolUtil.Write(APIContainer, dvAddr, new byte[] { atkDef, spdSpc });

            // Verification
            if (opponentPokemon)
            {
                return GetOpponent();
            }

            return GetPartyPokemon(pokemon.MemoryLocation.Offset / pokemon.MemoryLocation.Size) ?? pokemon;
        }
    }
}
