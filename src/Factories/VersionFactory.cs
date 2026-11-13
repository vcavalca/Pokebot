using BizHawk.Client.Common;
using Pokebot.Exceptions;
using Pokebot.Factories.Versions;
using Pokebot.Models.ActionRunners;
using Pokebot.Models.Config;
using Pokebot.Models.Memory;
using System;
using System.Linq;

namespace Pokebot.Factories
{
    public static class VersionFactory
    {
        public static GameVersion Create(AppConfig config, ApiContainer apiContainer, string hash)
        {
            if (config != null)
            {
                var tupleVersion = config.Versions.Select(x =>
                {
                    var hashFound = x.Hashs.FirstOrDefault(y => y.Hash.Equals(hash, StringComparison.InvariantCultureIgnoreCase));
                    if (hashFound != null)
                    {
                        return new Tuple<Models.Config.VersionInfo, HashData>(x, hashFound);
                    }

                    return null;
                }).FirstOrDefault(x => x != null);

                if (tupleVersion != null && tupleVersion.Item1 != null && tupleVersion.Item2 != null)
                {
                    var version = tupleVersion.Item1;
                    var hashData = tupleVersion.Item2;

                    // Gen 1-3 read memory through pret symbol files; the DS / 3DS games use addresses from their config
                    if (version.Generation <= 3 && string.IsNullOrEmpty(hashData.Symbols?.Main))
                    {
                        throw new PokebotException(Messages.Symbols_Empty);
                    }

                    var generation = config.Generations.FirstOrDefault(x => x.Code == version.Generation);
                    if (generation != null)
                    {
                        IGameMemory? memory = null;
                        IActionRunner? runner = null;

                        var versionType = (VersionCode)version.Code;
                        switch (versionType)
                        {
                            case VersionCode.Emerald:
                            case VersionCode.Sapphire:
                            case VersionCode.Ruby:
                                memory = new Gen3Memory(apiContainer, version, hashData, generation);
                                runner = new EmeraldRubySapphireActionRunner(apiContainer, memory);
                                break;
                            case VersionCode.LeafGreen:
                            case VersionCode.FireRed:
                                memory = new Gen3Memory(apiContainer, version, hashData, generation);
                                runner = new FireRedLeafGreenActionRunner(apiContainer, memory);
                                break;
                            case VersionCode.Gold:
                            case VersionCode.Silver:
                            case VersionCode.Crystal:
                                memory = new Gen2Memory(apiContainer, version, hashData, generation);
                                runner = new GoldSilverActionRunner(apiContainer, memory);
                                break;
                            case VersionCode.Red:
                            case VersionCode.Blue:
                            case VersionCode.Yellow:
                                var gen1Memory = new Gen1Memory(apiContainer, version, hashData, generation);
                                memory = gen1Memory;
                                runner = new RedBlueYellowActionRunner(apiContainer, gen1Memory);
                                break;
                            case VersionCode.Diamond:
                            case VersionCode.Pearl:
                            case VersionCode.Platinum:
                            case VersionCode.HeartGold:
                            case VersionCode.SoulSilver:
                            case VersionCode.Black:
                            case VersionCode.White:
                            case VersionCode.Black2:
                            case VersionCode.White2:
                                var ndsMemory = new NdsMemory(apiContainer, version, hashData, generation);
                                memory = ndsMemory;
                                runner = new NdsActionRunner(apiContainer, ndsMemory);
                                break;
                            case VersionCode.X:
                            case VersionCode.Y:
                            case VersionCode.OmegaRuby:
                            case VersionCode.AlphaSapphire:
                            case VersionCode.Sun:
                            case VersionCode.Moon:
                            case VersionCode.UltraSun:
                            case VersionCode.UltraMoon:
                                var threeDsMemory = new ThreeDsMemory(apiContainer, version, hashData, generation);
                                memory = threeDsMemory;
                                runner = new NdsActionRunner(apiContainer, threeDsMemory);
                                break;
                        }

                        if (memory == null || runner == null)
                        {
                            throw new NotSupportedException(Messages.Rom_NotSupported);
                        }

                        return new GameVersion(apiContainer, version, hashData, generation, memory, runner);
                    }
                }
            }

            throw new NotSupportedException(Messages.Rom_NotSupported);
        }
    }
}
