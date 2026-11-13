using System.Collections.Generic;

namespace Pokebot.Models.Config
{
    public class VersionInfo
    {
        public string Name { get; set; }
        public int Code { get; set; }
        public List<HashData> Hashs { get; set; }
        public int Generation { get; set; }
        public List<PokemonStarterConfig> Starters { get; set; }
        public List<PokeFinderType> PokeFinderTypes { get; set; }
        /// <summary>
        /// DS / 3DS games: memory locations (Gen 4 keeps most data in the heap, so they are pointer based there).
        /// Null for Gen 1-3, which use symbol files.
        /// </summary>
        public NdsPointers? Pointers { get; set; }
        public BotConfig? BotsConfig { get; set; }
    }
}
