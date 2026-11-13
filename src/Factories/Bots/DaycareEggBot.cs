using BizHawk.Client.Common;
using Pokebot.Exceptions;
using Pokebot.Factories.Versions;
using Pokebot.Models;
using Pokebot.Models.Memory;
using Pokebot.Models.Player;
using Pokebot.Panels;
using Pokebot.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Pokebot.Factories.Bots
{
    /// <summary>
    /// Shiny Day-Care eggs (Gen 4). The game tries to make an egg each time the steps of the second Day-Care
    /// Pokemon reach 255 (mod 256): the LCRNG decides if there is an egg (compatibility) and the Mersenne Twister
    /// gives its PID, which decides if it is shiny (IVs come later, when the egg is taken).
    /// The bot walks until just before that check, saves a state, then reloads it with new LCRNG and MT states
    /// until the egg the Day-Care Man gets is shiny for the player.
    /// Gen 5 only decides the egg when it is taken (PID and nature from the LCRNG, IVs from the MT, verified in
    /// Black): the player stands in front of the Day-Care Man while he has an egg, and the bot takes it from a saved
    /// state with new random numbers until it is shiny.
    /// </summary>
    public class DaycareEggBot : IBot
    {
        /// <summary>
        /// Second slot steps (low byte) where the bot stops walking. The step in progress may still count, so the
        /// state is saved at 252 to 254 (never 255: the check would already have happened). The egg appeared
        /// 2 frames (Platinum) / 1 frame (HeartGold) after the steps reached 255 in our tests.
        /// </summary>
        private static readonly uint[] _readySteps = { 252, 253 };
        private const uint LastSaveSteps = 254;
        /// <summary>Calls without moving before saving the state (the last step has to finish).</summary>
        private const int StopStillCalls = 6;
        /// <summary>Calls to wait after the check (steps = 255) before deciding that there is no egg.</summary>
        private const int CheckSettleCalls = 10;
        /// <summary>
        /// Frames without moving before turning around: short, nothing but walls stops the player here
        /// (the egg hatching walk waits longer because of the hatching texts).
        /// </summary>
        private const int WalkBlockedFrames = 12;
        /// <summary>Tries in a row without any egg before stopping: 0.8^100 with the worst compatibility (20%).</summary>
        private const int MaxTriesWithoutEgg = 100;
        /// <summary>Gen 5: calls (frames) of A presses without getting the egg before stopping (~190 in Black).</summary>
        private const int MaxReceiveCalls = 1500;
        /// <summary>Gen 5: calls the received egg must stay readable before it is checked.</summary>
        private const int ReceivedStableCalls = 10;

        private enum Phase
        {
            Preparing,
            Stopping,
            Trying,
            /// <summary>Gen 5: talking to the Day-Care Man until the egg is in the party.</summary>
            Receiving,
        }

        public bool Enabled { get; private set; }
        public ApiContainer APIContainer { get; }
        public GameVersion GameVersion { get; }
        public EggHatchControl Control { get; }

        public event IBot.PokemonEncounterEventHandler? PokemonEncountered;
        public event IBot.StateChangedEventHandler? StateChanged;
        public event IBot.PokemonFoundEventHandler? PokemonFound;

        private Phase _phase;
        private (uint X, uint Z)? _lastPosition;
        private int _stillCalls;
        private int _afterCheckCalls;
        private int _tryCalls;
        private int _tries;
        private int _eggs;
        private int _triesWithoutEgg;
        private HashSet<uint> _partyPids = new HashSet<uint>();
        private int _partyCountBefore;
        private int _receiveCalls;
        private int _receivedCalls;

        public DaycareEggBot(ApiContainer apiContainer, GameVersion gameVersion)
        {
            Enabled = false;
            APIContainer = apiContainer;
            GameVersion = gameVersion;

            Control = new EggHatchControl((gameVersion.Memory as NdsMemory)?.EggDecidedWhenReceived == true ? Messages.BotDaycareEgg_HelpGen5 : Messages.BotDaycareEgg_Help);
            Control.Dock = DockStyle.Fill;
        }

        private NdsMemory Memory => GameVersion.Memory as NdsMemory ?? throw new BotException(Messages.BotFactory_NotSupported);

        public void Start()
        {
            var memory = Memory;
            if (memory.EggDecidedWhenReceived)
            {
                StartReceiving(memory);
                return;
            }

            if (memory.GetGameState() != GameState.Overworld)
            {
                throw new BotException(Messages.SpinBot_StartOnlyMap);
            }

            var daycare = memory.GetDaycare();
            if (daycare.Count < 2)
            {
                throw new BotException(Messages.BotDaycareEgg_NeedTwo);
            }

            if (memory.GetDaycareOffspring() != 0)
            {
                throw new BotException(Messages.BotDaycareEgg_EggWaiting);
            }

            Log.Info($"Day-Care: {daycare[0].RealName} ({daycare[0].GetGenderMessage()}) + {daycare[1].RealName} ({daycare[1].GetGenderMessage()})");
            _phase = Phase.Preparing;
            _lastPosition = null;
            _tries = 0;
            _eggs = 0;
            _triesWithoutEgg = 0;
            GameVersion.Runner.ClearInputs();

            Enabled = true;
            StateChanged?.Invoke(Enabled);
        }

        /// <summary>
        /// Gen 5: the egg is decided when it is taken, so the state is saved in front of the Day-Care Man.
        /// </summary>
        private void StartReceiving(NdsMemory memory)
        {
            if (memory.GetGameState() != GameState.Overworld)
            {
                throw new BotException(Messages.SpinBot_StartOnlyMap);
            }

            var daycare = memory.GetDaycare();
            if (daycare.Count < 2)
            {
                throw new BotException(Messages.BotDaycareEgg_NeedTwo);
            }

            if (memory.GetDaycareOffspring() == 0)
            {
                throw new BotException(Messages.BotDaycareEgg_NoEggYet);
            }

            if (memory.GetPartyCount() >= 6)
            {
                throw new BotException(Messages.BotDaycareEgg_PartyFull);
            }

            Log.Info($"Day-Care: {daycare[0].RealName} ({daycare[0].GetGenderMessage()}) + {daycare[1].RealName} ({daycare[1].GetGenderMessage()})");
            _partyCountBefore = memory.GetPartyCount();
            _partyPids = new HashSet<uint>(memory.GetParty().Select(x => x.PID));
            APIContainer.EmuClient.SaveState(GetSaveStateName());
            GameVersion.Runner.ClearInputs();
            RandomizeEgg(memory);
            _phase = Phase.Receiving;
            _tries = 0;
            _eggs = 0;
            _receiveCalls = 0;
            _receivedCalls = 0;

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
                // Wild battle while walking
                GameVersion.Runner.Escape();
                return;
            }

            if (state != GameState.Overworld)
            {
                return;
            }

            var memory = Memory;
            if (_phase == Phase.Receiving)
            {
                Receive(memory);
                return;
            }

            var steps = memory.GetDaycareSteps() & 0xFF;
            var offspring = memory.GetDaycareOffspring();

            switch (_phase)
            {
                case Phase.Preparing:
                    if (offspring != 0)
                    {
                        // The game made an egg before the state was saved (it can't be changed anymore)
                        CheckEgg(offspring, false);
                        return;
                    }

                    if (_readySteps.Contains(steps))
                    {
                        _phase = Phase.Stopping;
                        _stillCalls = 0;
                        _lastPosition = null;
                        return;
                    }

                    GameVersion.Runner.HatchEggStep(WalkBlockedFrames);
                    return;

                case Phase.Stopping:
                    var position = memory.GetRawPosition();
                    if (_lastPosition == position)
                    {
                        _stillCalls++;
                    }
                    else
                    {
                        _lastPosition = position;
                        _stillCalls = 0;
                    }

                    if (_stillCalls < StopStillCalls)
                    {
                        return;
                    }

                    if (offspring != 0 || steps < _readySteps[0] || steps > LastSaveSteps)
                    {
                        // Went one step too far: walk to the next check
                        _phase = Phase.Preparing;
                        return;
                    }

                    APIContainer.EmuClient.SaveState(GetSaveStateName());
                    Log.Info($"Day-Care Egg: state saved {255 - steps} steps before the egg check, trying...");
                    GameVersion.Runner.ClearInputs();
                    RandomizeEgg(memory);
                    _afterCheckCalls = 0;
                    _phase = Phase.Trying;
                    return;

                case Phase.Trying:
                    _tryCalls++;
                    if (offspring != 0)
                    {
                        Log.Debug($"Day-Care Egg: egg {_afterCheckCalls} calls after the check, try took {_tryCalls} calls");
                        CheckEgg(offspring, true);
                        return;
                    }

                    // The check happens when the steps reach 255 and the egg shows up 1-2 frames later. The next step can
                    // count before the wait is over (running takes 8 frames per step: 256 reads 0), so once 255 was
                    // seen the wait goes on whatever the steps are.
                    if (steps == 255 || _afterCheckCalls > 0)
                    {
                        if (++_afterCheckCalls < CheckSettleCalls)
                        {
                            return;
                        }

                        Log.Debug($"Day-Care Egg: no egg, try took {_tryCalls} calls");

                        // The compatibility roll failed: no egg this time
                        if (++_triesWithoutEgg >= MaxTriesWithoutEgg)
                        {
                            throw new BotException(string.Format(Messages.BotDaycareEgg_NoEgg, _triesWithoutEgg));
                        }

                        Reload();
                        return;
                    }

                    if (steps < _readySteps[0])
                    {
                        // Past the check without seeing it: start again from the state
                        Log.Debug($"Day-Care Egg: steps {steps} past the check, reloading");
                        Reload();
                        return;
                    }

                    GameVersion.Runner.HatchEggStep(WalkBlockedFrames);
                    return;
            }
        }

        /// <summary>
        /// Gen 5: A through the Day-Care Man's texts ("You do want it, yes?" is answered YES) until the egg is in the
        /// party, then checks it.
        /// </summary>
        private void Receive(NdsMemory memory)
        {
            if (memory.GetPartyCount() > _partyCountBefore)
            {
                // The party data can be caught mid-update: wait until the new egg reads fine for a while
                var egg = memory.GetParty().FirstOrDefault(x => !_partyPids.Contains(x.PID));
                if (egg == null)
                {
                    _receivedCalls = 0;
                    return;
                }

                if (++_receivedCalls < ReceivedStableCalls)
                {
                    return;
                }

                Log.Debug($"Day-Care Egg: egg received after {_receiveCalls} calls");
                _eggs++;
                PokemonEncountered?.Invoke(egg);
                if (egg.IsShiny)
                {
                    Log.Warn(Messages.BotDaycareEgg_ShinyReceived);
                    PokemonFound?.Invoke(egg);
                    Stop();
                    return;
                }

                Reload();
                return;
            }

            if (++_receiveCalls > MaxReceiveCalls)
            {
                throw new BotException(string.Format(Messages.BotDaycareEgg_NotReceived, MaxReceiveCalls));
            }

            GameVersion.Runner.ReceivePokemon();
        }

        private void CheckEgg(uint pid, bool canReload)
        {
            var memory = Memory;
            _eggs++;
            _triesWithoutEgg = 0;
            var shiny = memory.IsShinyForPlayer(pid);
            var nature = GameVersion.GenerationInfo.Natures.FirstOrDefault(x => x.Id == pid % 25);
            var line = string.Join(" | ",
                $"#{_eggs} Egg",
                shiny ? Messages.Viewer_Shiny.ToUpperInvariant() : Messages.Viewer_NotShiny,
                string.Format(Messages.Viewer_Nature, nature?.Name ?? "N/A"),
                string.Format(Messages.Viewer_PID, pid.ToString("X8")),
                $"try {_tries + 1}");

            if (shiny)
            {
                Log.Warn(line);
                Log.Warn(Messages.BotDaycareEgg_ShinyFound);
                Stop();
                return;
            }

            Log.Info(line);
            if (!canReload)
            {
                // Not shiny and made before the state existed: it has to be taken (or refused) first
                throw new BotException(Messages.BotDaycareEgg_EggWaiting);
            }

            Reload();
        }

        private void Reload()
        {
            if (APIContainer.EmuClient.LoadOrStop(GetSaveStateName()))
            {
                _tries++;
                _afterCheckCalls = 0;
                _tryCalls = 0;
                _receiveCalls = 0;
                _receivedCalls = 0;
                GameVersion.Runner.ClearInputs();
                RandomizeEgg(Memory);
            }
        }

        /// <summary>
        /// New LCRNG and MT states (Gen 4: the egg check; Gen 5: the egg PID, nature and IVs), and in B2W2 a new
        /// Day-Care egg seed in case the egg is made from it there.
        /// </summary>
        private static void RandomizeEgg(NdsMemory memory)
        {
            memory.RandomizeCurrentSeed();
            memory.RandomizeDaycareEggSeed();
        }

        private string GetSaveStateName()
        {
            return $"{GameVersion.VersionInfo.Name}_daycare_egg";
        }

        public UserControl GetPanel()
        {
            return Control;
        }

        public bool UseDelay()
        {
            // The walk holds a direction: run every frame
            return false;
        }

        public void UpdateUI(GameState state)
        {
        }
    }
}
