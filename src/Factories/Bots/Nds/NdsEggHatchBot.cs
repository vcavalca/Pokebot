using BizHawk.Client.Common;
using Pokebot.Exceptions;
using Pokebot.Factories.Versions;
using Pokebot.Models;
using Pokebot.Models.Player;
using Pokebot.Panels;
using Pokebot.Utils;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Pokebot.Factories.Bots
{
    /// <summary>
    /// Walks back and forth until every egg in the party hatches. There are no filters: the egg's DVs / PID
    /// are decided when it is received (Gen 3 IVs too), so hatching can't change the result.
    /// Each hatched Pokemon is added to the stats and shiny ones are notified.
    /// </summary>
    public class NdsEggHatchBot : IBot
    {
        // The egg flag changes when the hatch scene starts and the rest of the data (nickname, met info...) changes
        // several times during the scene. In Platinum the player position reads (0, 0) during the scene: the hatched
        // Pokemon is reported once the player is back on the map (after the nickname question, answered NO by the
        // runner's B taps). Without that sign, once the data stayed the same HatchedStableCalls calls.
        private const int HatchedStableCalls = 180;
        private const int HatchedBackOnMapCalls = 30;
        private const int HatchedMaxCalls = 3000;

        public bool Enabled { get; private set; }
        public ApiContainer APIContainer { get; }
        public GameVersion GameVersion { get; }
        public EggHatchControl Control { get; }

        public event IBot.PokemonEncounterEventHandler? PokemonEncountered;
        public event IBot.StateChangedEventHandler? StateChanged;
        public event IBot.PokemonFoundEventHandler? PokemonFound;

        // Eggs are followed by PID, not by slot: a slot can be skipped for a frame while the game re-encrypts it
        private readonly HashSet<uint> _eggPids;
        private uint? _hatchedPid;
        private int _hatchedChecksum;
        private int _hatchedWaitCalls;
        private int _hatchedStableCalls;
        private bool _hatchSceneSeen;

        public NdsEggHatchBot(ApiContainer apiContainer, GameVersion gameVersion)
        {
            Enabled = false;
            APIContainer = apiContainer;
            GameVersion = gameVersion;
            _eggPids = new HashSet<uint>();

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

            _eggPids.Clear();
            _hatchedPid = null;
            foreach (var pokemon in GameVersion.Memory.GetParty())
            {
                if (pokemon.IsEgg)
                {
                    _eggPids.Add(pokemon.PID);
                }
            }

            if (_eggPids.Count == 0)
            {
                throw new BotException(Messages.BotEggHatch_NoEgg);
            }

            GameVersion.Runner.ClearInputs();
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

            if (state != GameState.Overworld)
            {
                return;
            }

            CheckHatchedEggs(playerData);
            if (Enabled)
            {
                GameVersion.Runner.HatchEggStep();
            }
        }

        private void CheckHatchedEggs(PlayerData playerData)
        {
            var party = GameVersion.Memory.GetParty();
            if (_hatchedPid == null)
            {
                var hatching = party.FirstOrDefault(x => _eggPids.Contains(x.PID) && !x.IsEgg);
                if (hatching != null)
                {
                    _hatchedPid = hatching.PID;
                    _hatchedChecksum = hatching.Checksum;
                    _hatchedWaitCalls = 0;
                    _hatchedStableCalls = 0;
                    _hatchSceneSeen = false;
                    Log.Debug($"Egg {hatching.PID:X8} is hatching ({hatching.RealName})");
                }

                return;
            }

            var hatched = party.FirstOrDefault(x => x.PID == _hatchedPid.Value);
            if (hatched != null && hatched.Checksum != _hatchedChecksum)
            {
                _hatchedChecksum = hatched.Checksum;
                _hatchedStableCalls = 0;
            }
            else
            {
                _hatchedStableCalls++;
            }

            var inScene = playerData.Position.X == 0 && playerData.Position.Y == 0;
            if (inScene)
            {
                _hatchSceneSeen = true;
                _hatchedStableCalls = 0;
            }

            var done = _hatchSceneSeen ? _hatchedStableCalls >= HatchedBackOnMapCalls : _hatchedStableCalls >= HatchedStableCalls;
            if (!done && ++_hatchedWaitCalls < HatchedMaxCalls)
            {
                return;
            }

            _eggPids.Remove(_hatchedPid.Value);
            _hatchedPid = null;
            if (hatched != null)
            {
                // The egg was decided when it was received: just report it (stats, shiny notification)
                Log.Info($"{hatched.RealName} hatched");
                PokemonEncountered?.Invoke(hatched);
                if (hatched.IsShiny)
                {
                    Log.Warn(Messages.BotEggHatch_ShinyHatched);
                    PokemonFound?.Invoke(hatched);
                }
            }

            if (_eggPids.Count == 0)
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
