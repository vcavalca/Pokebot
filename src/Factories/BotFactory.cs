using BizHawk.Client.Common;
using Pokebot.Factories.Bots;
using Pokebot.Factories.Versions;
using Pokebot.Models.Config;
using System;

namespace Pokebot.Factories
{
    public static class BotFactory
    {
        public static IBot Create(BotCode code, ApiContainer apiContainer, GameVersion gameVersion)
        {
            switch (code)
            {
                case BotCode.Starter:
                    return new StarterBot(apiContainer, gameVersion);
                case BotCode.Spin:
                    return new SpinBot(apiContainer, gameVersion);
                case BotCode.Static:
                    return new LegendaryBot(apiContainer, gameVersion);
                case BotCode.PokeFinder:
                    return new PokeFinderBot(apiContainer, gameVersion);
                case BotCode.PickupPokemon:
                    return new PickupPokemonBot(apiContainer, gameVersion);
                // The DS / 3DS games have their own fishing, path and egg hatching bots (frame paced runner, PID based)
                case BotCode.Fishing:
                    return gameVersion.IsDsGame ? new NdsFishingBot(apiContainer, gameVersion) : new FishingBot(apiContainer, gameVersion);
                case BotCode.Path:
                    return gameVersion.IsDsGame ? new NdsPathBot(apiContainer, gameVersion) : new PathBot(apiContainer, gameVersion);
                case BotCode.EggHatch:
                    return gameVersion.IsDsGame ? new NdsEggHatchBot(apiContainer, gameVersion) : new EggHatchBot(apiContainer, gameVersion);
                case BotCode.DaycareEgg:
                    return new DaycareEggBot(apiContainer, gameVersion);
            }

            throw new NotSupportedException(Messages.BotFactory_NotSupported);
        }
    }
}
