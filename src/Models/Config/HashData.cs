using Newtonsoft.Json;

namespace Pokebot.Models.Config
{
    public class HashData
    {
        /// <summary>
        /// The hash BizHawk reports for the ROM (SHA1 for GB/GBA, MD5 for NDS/3DS, same as BizHawk's gamedb).
        /// </summary>
        public string Hash { get; }
        public bool Tested { get; }
        /// <summary>pret symbol files (Gen 1-3 only, null for the DS / 3DS games).</summary>
        public HashSymbols? Symbols { get; }

        [JsonConstructor]
        public HashData(string hash, bool tested, HashSymbols? symbols = null)
        {
            Hash = hash;
            Tested = tested;
            Symbols = symbols;
        }
    }
}
