using BizHawk.Client.Common;
using Pokebot.Factories.Versions;
using Pokebot.Models;
using Pokebot.Models.Player;
using Pokebot.Models.Pokemons;

namespace Pokebot.Factories.Bots
{
    public class LegendaryBot : StaticBot
    {
        public LegendaryBot(ApiContainer apiContainer, GameVersion gameVersion) : base(apiContainer, gameVersion)
        {
        }

        public override void Execute(PlayerData playerData, GameState state)
        {
            if (state == GameState.Battle)
            {
                Pokemon pokemon = GameVersion.Memory.GetOpponent();
                Check(pokemon);
            }
            else if (GameVersion.IsDsGame)
            {
                if (state == GameState.Overworld)
                {
                    // Talks to the Pokemon and advances its texts until the battle starts
                    GameVersion.Runner.Interact();
                }
            }
            else
            {
                APIContainer.Joypad.Set("A", true);
            }
        }
    }
}
