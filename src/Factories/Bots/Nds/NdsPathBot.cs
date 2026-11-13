using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using BizHawk.Client.Common;
using Pokebot.Exceptions;
using Pokebot.Factories.Versions;
using Pokebot.Models;
using Pokebot.Models.Player;
using Pokebot.Models.Pokemons;
using Pokebot.Panels;
using Pokebot.Utils;

namespace Pokebot.Factories.Bots
{
    internal class NdsPathBot : IBot
    {
        public bool Enabled { get; private set; }

        public event IBot.PokemonEncounterEventHandler? PokemonEncountered;
        public event IBot.PokemonFoundEventHandler? PokemonFound;
        public event IBot.StateChangedEventHandler? StateChanged;

        private List<uint> _seedsHistory;

        public ApiContainer APIContainer { get; }
        public GameVersion GameVersion { get; }
        public PathControl Control { get; }

        private int _pathIndex;
        private Pokemon? _lastEncountered;

        public NdsPathBot(ApiContainer apiContainer, GameVersion gameVersion)
        {
            Enabled = false;
            APIContainer = apiContainer;
            GameVersion = gameVersion;

            _pathIndex = 0;
            _lastEncountered = null;
            _seedsHistory = new List<uint>();

            Control = new PathControl();
            Control.Dock = DockStyle.Fill;
            Control.SetFilterPanel(gameVersion.GenerationInfo);
            Control.FilterPanel.SetShinyHackVisible(gameVersion.Memory.CanSetShiny());
        }

        public void Start()
        {
            if (Control.GetPath().Count == 0)
            {
                throw new BotException(Messages.PathBot_EmptyPath);
            }

            var state = GameVersion.Memory.GetGameState();
            if (state != GameState.Overworld)
            {
                throw new BotException(Messages.SpinBot_StartOnlyMap);
            }

            bool shouldLoad = false;
            if (APIContainer.HasSaveState(GetSaveStateName()))
            {
                var result = MessageBox.Show(Messages.Bot_FileExistReplaceMessage, Messages.Bot_FileExistReplaceTitle, MessageBoxButtons.YesNo);
                shouldLoad = result == DialogResult.Yes;
            }

            bool loaded = false;
            if (shouldLoad)
            {
                try
                {
                    loaded = APIContainer.EmuClient.LoadState(GetSaveStateName());
                }
                catch (FileNotFoundException) { }
            }

            if (!loaded)
            {
                APIContainer.EmuClient.SaveState(GetSaveStateName());
            }

            _pathIndex = 0;
            _lastEncountered = null;
            GameVersion.Runner.ClearInputs();
            UpdateRNG();
            Enabled = true;
            StateChanged?.Invoke(Enabled);
        }

        public void Stop()
        {
            Enabled = false;
            StateChanged?.Invoke(Enabled);
        }

        public void Execute(PlayerData playerData, GameState state)
        {
            if (state == GameState.Overworld)
            {
                ExecuteOverworld(playerData);
                return;
            }

            if (state == GameState.Battle)
            {
                ExecuteBattle();
            }
        }

        private void ExecuteOverworld(PlayerData playerData)
        {
            var path = Control.GetPath();
            _lastEncountered = null;
            if (_pathIndex < path.Count)
            {
                // The runner walks one tile per direction (or presses A) and tells when the step is over
                if (GameVersion.Runner.PathStep(path[_pathIndex]))
                {
                    _pathIndex++;
                }
            }
            else
            {
                // End of the path: talk to / interact with what is in front until a battle starts
                GameVersion.Runner.Interact();
            }
        }

        private void ExecuteBattle()
        {
            Pokemon pokemon = GameVersion.Memory.GetOpponent();
            if (GameVersion.Memory.CanSetShiny() && Control.FilterPanel.IsShinyHackEnabled() && !pokemon.IsShiny)
            {
                pokemon = GameVersion.Memory.SetShiny(pokemon);
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
                if (APIContainer.EmuClient.LoadOrStop(GetSaveStateName()))
                {
                    GameVersion.Runner.ClearInputs();
                    UpdateRNG();
                    Log.Debug($"Path: state reloaded, RNG {GameVersion.Memory.GetCurrentSeed():X8}");
                    _pathIndex = 0;
                    _lastEncountered = null;
                }
            }
        }

        private string GetSaveStateName()
        {
            return $"{GameVersion.VersionInfo.Name}_path";
        }

        public UserControl GetPanel()
        {
            return Control;
        }

        public bool UseDelay()
        {
            // The DS action runner paces its inputs by frames, the bot runs every frame
            return false;
        }

        private void UpdateRNG()
        {
            uint random;
            do
            {
                random = GameVersion.Memory.RandomizeCurrentSeed();
            } while (_seedsHistory.Contains(random));

            _seedsHistory.Add(random);
        }

        public void UpdateUI(GameState state) { }
    }
}
