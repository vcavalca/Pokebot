using BizHawk.Client.Common;
using Pokebot.Exceptions;
using Pokebot.Factories.Versions;
using Pokebot.Models;
using Pokebot.Models.Player;
using Pokebot.Models.Pokemons;
using Pokebot.Panels;
using Pokebot.Utils;
using System;
using System.Windows.Forms;

namespace Pokebot.Factories.Bots
{
    internal class NdsFishingBot : IBot
    {
        public bool Enabled { get; private set; }

        public event IBot.PokemonEncounterEventHandler? PokemonEncountered;
        public event IBot.PokemonFoundEventHandler? PokemonFound;
        public event IBot.StateChangedEventHandler? StateChanged;

        public ApiContainer APIContainer { get; }
        public GameVersion GameVersion { get; }
        public SpinControl Control { get; }

        private Pokemon? _lastEncountered;

        public NdsFishingBot(ApiContainer apiContainer, GameVersion gameVersion)
        {
            Enabled = false;
            APIContainer = apiContainer;
            GameVersion = gameVersion;

            Control = new SpinControl();
            Control.Dock = DockStyle.Fill;
            Control.SetFilterPanel(GameVersion.GenerationInfo);
            Control.FilterPanel.SetShinyHackVisible(gameVersion.Memory.CanSetShiny());
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

            _lastEncountered = null;
            GameVersion.Runner.ClearInputs();
        }

        public void Stop()
        {
            Enabled = false;
            StateChanged?.Invoke(Enabled);
        }

        public void Execute(PlayerData playerData, GameState state)
        {
            if (state == GameState.Battle)
            {
                Pokemon pokemon = GameVersion.Memory.GetOpponent();
                if (GameVersion.Memory.CanSetShiny() && Control.FilterPanel.IsShinyHackEnabled() && !pokemon.IsShiny)
                {
                    GameVersion.Memory.SetShiny(pokemon);
                    return;
                }

                // By PID: the checksum changes during the battle (e.g. when the foe uses PP)
                if (_lastEncountered?.PID != pokemon.PID)
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
            else if (state == GameState.Overworld)
            {
                // Casts the registered rod, waits for a bite and reels in (stops the bot if fishing isn't possible)
                GameVersion.Runner.Fish();
                _lastEncountered = null;
            }
        }

        public UserControl GetPanel()
        {
            return Control;
        }

        public bool UseDelay()
        {
            return false;
        }

        public void UpdateUI(GameState state)
        {
        }
    }
}
