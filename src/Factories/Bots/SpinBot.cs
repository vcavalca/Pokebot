using BizHawk.Client.Common;
using Pokebot.Exceptions;
using Pokebot.Factories.Versions;
using Pokebot.Models;
using Pokebot.Models.Player;
using Pokebot.Models.Pokemons;
using Pokebot.Panels;
using Pokebot.Utils;
using System.Windows.Forms;

namespace Pokebot.Factories.Bots
{
    public class SpinBot : IBot
    {
        public bool Enabled { get; private set; }
        public ApiContainer APIContainer { get; }
        public GameVersion GameVersion { get; }
        public SpinControl Control { get; }

        public event IBot.PokemonEncounterEventHandler? PokemonEncountered;
        public event IBot.StateChangedEventHandler? StateChanged;
        public event IBot.PokemonFoundEventHandler? PokemonFound;

        private Pokemon? _lastEncountered;

        public SpinBot(ApiContainer apiContainer, GameVersion gameVersion)
        {
            Enabled = false;
            APIContainer = apiContainer;
            GameVersion = gameVersion;
            _lastEncountered = null;

            Control = new SpinControl();
            Control.Dock = DockStyle.Fill;
            Control.SetFilterPanel(gameVersion.GenerationInfo);
            Control.FilterPanel.SetShinyHackVisible(gameVersion.Memory.CanSetShiny());
        }

        public void Execute(PlayerData playerData, GameState state)
        {
            if (state == GameState.Overworld)
            {
                if (!GameVersion.Runner.Spin())
                {
                    throw new BotException(Messages.SpinBot_UnknowNextMove);
                }
                _lastEncountered = null;
            }
            else if (state == GameState.Battle || state == GameState.BagMenu)
            {
                Pokemon pokemon = GameVersion.Memory.GetOpponent();
                if (GameVersion.Memory.CanSetShiny() && Control.FilterPanel.IsShinyHackEnabled() && !pokemon.IsShiny)
                {
                    pokemon = GameVersion.Memory.SetShiny(pokemon);
                    return;
                }

                // DS / 3DS: by PID, the checksum changes during the battle (e.g. when the foe uses PP)
                var isNew = GameVersion.IsDsGame ? _lastEncountered?.PID != pokemon.PID : _lastEncountered?.Checksum != pokemon.Checksum;
                if (isNew)
                {
                    _lastEncountered = pokemon;
                    PokemonEncountered?.Invoke(pokemon);
                }

                if (Control.FilterPanel.Comparator.Compare(pokemon))
                {
                    Log.Warn($"{pokemon.RealName}: {Messages.Pokemon_FoundCatch}");
                    PokemonFound?.Invoke(pokemon);
                    Stop();
                }
                else
                {
                    GameVersion.Runner.Escape();
                }
            }
        }

        public void Start()
        {
            Enabled = true;
            StateChanged?.Invoke(Enabled);

            var state = GameVersion.Memory.GetGameState();
            if (state != GameState.Overworld)
            {
                throw new BotException(Messages.SpinBot_StartOnlyMap);
            }
        }

        public void Stop()
        {
            Enabled = false;
            StateChanged?.Invoke(Enabled);
        }

        public UserControl GetPanel()
        {
            return Control;
        }

        public bool UseDelay()
        {
            // The DS action runner paces its inputs by frames (held presses), so the bot runs every frame there
            return !GameVersion.IsDsGame;
        }

        public void UpdateUI(GameState state)
        {
        }
    }
}
