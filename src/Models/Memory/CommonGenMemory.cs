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
    public abstract class CommonGenMemory : IGameMemory
    {
        public ApiContainer APIContainer { get; }
        public VersionInfo VersionInfo { get; }
        public HashData HashData { get; }
        public GenerationInfo GenerationInfo { get; }

        public CommonGenMemory(ApiContainer apiContainer, VersionInfo versionInfo, HashData hashData, GenerationInfo generationInfo)
        {
            APIContainer = apiContainer;
            VersionInfo = versionInfo;
            HashData = hashData;
            GenerationInfo = generationInfo;
            Symbols = LoadSymbols(hashData);
        }

        public IReadOnlyList<Symbol> Symbols { get; }

        public abstract int GetActionSelectionCursor();
        public abstract void TrySetEscape();
        public abstract uint GetCurrentSeed();
        public abstract GameState GetGameState();
        public abstract Pokemon GetOpponent();
        public abstract IReadOnlyList<Pokemon> GetParty();
        public abstract int GetPartyCount();
        public abstract PlayerData GetPlayer();
        public abstract int GetSID();
        public abstract ICollection<GTask> GetTasks();
        public abstract int GetTID();
        public abstract uint RandomizeCurrentSeed();
        public abstract FishingState GetFishingResult();

        public IReadOnlyList<Symbol> GetSymbols()
        {
            return Symbols;
        }

        public Symbol GetSymbol(string name)
        {
            return Symbols.FirstOrDefault(x => x.Name == name);
        }

        protected IReadOnlyList<Symbol> LoadSymbols(HashData hashData)
        {
            var symbols = new List<Symbol>();
            if (hashData.Symbols == null)
            {
                // DS / 3DS games: no symbol files, their addresses come from the game config (NdsPointers)
                return symbols;
            }

            Log.Debug($"Loading main symbol: {hashData.Symbols.Main}");
            //Load main symbol file
            var mainFileData = EmbeddedFiles.GetSymbolFile(hashData.Symbols.Main);
            if (mainFileData is byte[] mainBytes)
            {
                symbols = SymbolUtil.Load(mainBytes).ToList();

                //Load and replace extra symbols if they exists
                foreach (var file in hashData.Symbols.Patches)
                {
                    Log.Debug($"Loading patch symbol: {file}");
                    var data = EmbeddedFiles.GetSymbolFile(file);
                    if (data is byte[] bytes)
                    {
                        var tempSymbols = SymbolUtil.Load(bytes);
                        foreach (var tempSymbol in tempSymbols)
                        {
                            var symbolFound = symbols.FirstOrDefault(x => x.Name == tempSymbol.Name);
                            if (symbolFound != null)
                            {
                                symbols.Remove(symbolFound);

                                long address = tempSymbol.Address;
                                char letter = symbolFound.Letter;
                                string name = symbolFound.Name;
                                int size;
                                if (tempSymbol.Letter == 'c') //c is custom, no changes except for address
                                {
                                    size = symbolFound.Size;
                                }
                                else
                                {
                                    size = tempSymbol.Size;
                                }

                                symbols.Add(new Symbol(address, letter, size, name));
                            }
                            else if (tempSymbol.Letter == 'a') //add custom line
                            {
                                symbols.Add(tempSymbol);
                            }
                        }
                    }
                    else
                    {
                        Log.Error($"Failed to load patch: {file}");
                    }
                }
            }

            return symbols;
        }

        protected string GetBytesText(byte[] bytes)
        {
            StringBuilder sb = new StringBuilder();

            foreach (var b in bytes)
            {
                sb.Append(GenerationInfo.Characters[b]);
            }

            return sb.ToString();
        }

        public virtual bool CanSetShiny()
        {
            return false;
        }

        public virtual Pokemon SetShiny(Pokemon pokemon)
        {
            throw new System.NotImplementedException();
        }

        #region DS / 3DS memory access

        /// <summary>
        /// BizHawk's melonDS memory domain that maps the whole ARM9 address space (main RAM is 0x02000000-0x023FFFFF).
        /// </summary>
        public const string Arm9Bus = "ARM9 System Bus";

        /// <summary>BizHawk's Encore (3DS) memory domain with the whole FCRAM (game heap, 256 MB in New 3DS mode).</summary>
        public const string Fcram = "FCRAM";

        /// <summary>
        /// Memory domain the DS helpers below read from: the ARM9 bus for the DS games, the FCRAM for the 3DS ones.
        /// (The Gen 1-3 memories read their symbols' own domains.)
        /// </summary>
        public virtual string Domain => Arm9Bus;

        public byte[] ReadBytes(long address, int size)
        {
            return APIContainer.Memory.ReadByteRange(address, size, Domain).ToArray();
        }

        public byte ReadU8(long address)
        {
            return ReadBytes(address, 1)[0];
        }

        public ushort ReadU16(long address)
        {
            return System.BitConverter.ToUInt16(ReadBytes(address, 2), 0);
        }

        public uint ReadU32(long address)
        {
            return System.BitConverter.ToUInt32(ReadBytes(address, 4), 0);
        }

        public void WriteU16(long address, ushort value)
        {
            APIContainer.Memory.WriteU16(address, value, Domain);
        }

        public void WriteU32(long address, uint value)
        {
            APIContainer.Memory.WriteU32(address, value, Domain);
        }

        /// <summary>
        /// True if the value looks like a pointer into the DS main RAM.
        /// </summary>
        public static bool IsMainRamPointer(long value)
        {
            return value >= 0x02000000 && value < 0x02400000;
        }

        #endregion
    }
}
