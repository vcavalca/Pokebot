using BizHawk.Client.Common;
using Pokebot.Exceptions;
using Pokebot.Factories.Versions;
using Pokebot.Models;
using Pokebot.Models.Player;
using Pokebot.Panels;
using Pokebot.Utils;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Pokebot.Factories.Bots
{
    /// <summary>
    /// Walks back and forth until every egg in the party hatches. There are no filters: the egg's DVs / PID
    /// are decided when it is received (Gen 3 IVs too), so hatching can't change the result.
    /// Each hatched Pokemon is added to the stats and shiny ones are notified.
    /// </summary>
    public class EggHatchBot : IBot
    {
        // Bot calls to wait after an egg hatched before reading it (hatch scene updates the party)
        private const int HatchedSettleCalls = 120;

        public bool Enabled { get; private set; }
        public ApiContainer APIContainer { get; }
        public GameVersion GameVersion { get; }
        public EggHatchControl Control { get; }

        public event IBot.PokemonEncounterEventHandler? PokemonEncountered;
        public event IBot.StateChangedEventHandler? StateChanged;
        public event IBot.PokemonFoundEventHandler? PokemonFound;

        private readonly HashSet<int> _eggSlots;
        private int? _hatchedSlot;
        private int _hatchedWaitCalls;

        public EggHatchBot(ApiContainer apiContainer, GameVersion gameVersion)
        {
            Enabled = false;
            APIContainer = apiContainer;
            GameVersion = gameVersion;
            _eggSlots = new HashSet<int>();

            Control = new EggHatchControl();
            Control.Dock = DockStyle.Fill;
        }

        public void Start()
        {
            var state = GameVersion.Memory.GetGameState();
            if (state != GameState.Overworld)
            {
                throw new BotException(Messages.SpinBot_StartOnlyMap);
            }

            _eggSlots.Clear();
            _hatchedSlot = null;
            var party = GameVersion.Memory.GetParty();
            for (int i = 0; i < party.Count; i++)
            {
                if (party[i].IsEgg)
                {
                    _eggSlots.Add(i);
                }
            }

            if (_eggSlots.Count == 0)
            {
                throw new BotException(Messages.BotEggHatch_NoEgg);
            }

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
            if (state == GameState.Battle)
            {
                // Wild battle while walking in grass
                GameVersion.Runner.Escape();
                return;
            }

            CheckHatchedEggs();
            if (Enabled)
            {
                GameVersion.Runner.HatchEggStep();
            }
        }

        private void CheckHatchedEggs()
        {
            var party = GameVersion.Memory.GetParty();
            if (_hatchedSlot == null)
            {
                foreach (var slot in _eggSlots)
                {
                    if (slot < party.Count && !party[slot].IsEgg)
                    {
                        _hatchedSlot = slot;
                        _hatchedWaitCalls = 0;
                        break;
                    }
                }

                return;
            }

            if (++_hatchedWaitCalls < HatchedSettleCalls)
            {
                return;
            }

            var hatchedSlot = _hatchedSlot.Value;
            _hatchedSlot = null;
            _eggSlots.Remove(hatchedSlot);
            if (hatchedSlot >= party.Count)
            {
                return;
            }

            // The egg was decided when it was received: just report it (stats, shiny notification)
            var pokemon = party[hatchedSlot];
            PokemonEncountered?.Invoke(pokemon);
            if (pokemon.IsShiny)
            {
                Log.Warn(Messages.BotEggHatch_ShinyHatched);
                PokemonFound?.Invoke(pokemon);
            }

            if (_eggSlots.Count == 0)
            {
                Log.Info(Messages.BotEggHatch_AllHatched);
                Stop();
            }
        }

        public UserControl GetPanel()
        {
            return Control;
        }

        public bool UseDelay()
        {
            // The walk holds a direction: run every frame so it is not a series of taps
            return false;
        }

        public void UpdateUI(GameState state)
        {
        }
    }
}
