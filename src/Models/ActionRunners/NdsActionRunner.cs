using System;
using System.Collections.Generic;
using System.Linq;
using BizHawk.Client.Common;
using Pokebot.Exceptions;
using Pokebot.Models.Memory;
using Pokebot.Models.Player;
using Pokebot.Utils;

namespace Pokebot.Models.ActionRunners
{
    /// <summary>
    /// Inputs for Diamond / Pearl / Platinum / HeartGold / SoulSilver and Black / White / Black 2 / White 2.
    /// Inputs go through the timed input queue (see <see cref="CommonActionRunner.Update"/>): the game reads
    /// most inputs on alternate frames, so presses are held for a few frames.
    /// The bots call these methods every frame: each call queues the next input(s) of a routine and returns.
    /// </summary>
    public class NdsActionRunner : CommonActionRunner
    {
        #region Timings

        /// <summary>Battle menu state while FIGHT/BAG/RUN/POKEMON is shown.</summary>
        private const int BattleMainMenu = 1;

        /// <summary>
        /// Moves the battle cursor to RUN from any position: the first Down only shows the cursor (on FIGHT),
        /// the second goes to the bottom row, Left Left reaches BAG, Right is RUN. Same layout in Gen 5 (verified in Black).
        /// (BizHawk ignored touch coordinates set from code in our tests, so the touch screen isn't used.)
        /// </summary>
        private static readonly string[] _runSequence = { "Down", "Down", "Left", "Left", "Right", "A" };
        private const int MenuHoldFrames = 4;
        // With 6 the menu state sometimes still read "main menu" after the sequence and it was sent twice
        private const int MenuReleaseFrames = 10;

        /// <summary>Frames the main menu must be shown before pressing (it slides in when it appears).</summary>
        private const int MainMenuSettleFrames = 20;

        /// <summary>
        /// Pressing B while battle texts are shown skips their wait ("Wild X appeared!", "Got away safely!"...):
        /// about 250 frames saved per encounter in our Platinum tests.
        /// </summary>
        private const int TextSkipHoldFrames = 2;
        private const int TextSkipReleaseFrames = 6;

        /// <summary>Texts / dialogs: A (or B) held 4 frames then released 10 frames.</summary>
        private const int TextHoldFrames = 4;
        private const int TextReleaseFrames = 10;

        /// <summary>
        /// Turning in place: a short press (same timing as wyanido/pokebot-nds); a long one would walk.
        /// </summary>
        private const int TurnHoldFrames = 4;
        private const int TurnReleaseFrames = 4;

        /// <summary>
        /// Gen 5 "spin": one step each way. Turning in place doesn't trigger encounters there (1 battle in 4500 frames
        /// of turns in Black's grass). Measured in Black: a press of 6-8 frames only turns, 10-14 frames turn and walk
        /// exactly one tile.
        /// </summary>
        private const int StepHoldFrames = 12;
        // The step ends ~26 frames after the press starts: a new press before that is ignored
        private const int StepReleaseFrames = 20;

        /// <summary>
        /// Frames (from the start of a RUN sequence) before selecting RUN again if the main menu is still shown.
        /// The menu state leaves "main menu" up to ~70 frames after the A press; after "Can't escape!" the menu
        /// only comes back after the foe's turn, so a long wait costs nothing.
        /// </summary>
        private const int FramesBetweenRunAttempts = 240;

        /// <summary>
        /// Without a battle menu state (Ultra Sun/Moon) the RUN sequence is sent every this many frames: about the
        /// time of a text, short enough to not wait long once the menu shows.
        /// </summary>
        private const int FramesBetweenBlindRuns = 150;

        #endregion

        private static readonly PlayerFacingDirection[] _spinOrder =
        {
            PlayerFacingDirection.Down,
            PlayerFacingDirection.Left,
            PlayerFacingDirection.Up,
            PlayerFacingDirection.Right,
        };

        public NdsMemory Memory { get; }

        private int _spinIndex;
        private int? _lastRunFrame;
        private int? _mainMenuSinceFrame;
        private int _lastLoggedMenu = -1;
        private int? _lastBattleFrame;

        public NdsActionRunner(ApiContainer apiContainer, NdsMemory memory)
            : base(apiContainer, memory)
        {
            Memory = memory;
        }

        private int FrameCount => APIContainer.Emulation.FrameCount();

        /// <summary>
        /// Frames since <paramref name="since"/>, or null if unknown / in the future (a save state was loaded).
        /// </summary>
        private int? FramesSince(int? since)
        {
            if (since == null)
            {
                return null;
            }

            var elapsed = FrameCount - since.Value;
            return elapsed < 0 ? null : elapsed;
        }

        public override void ClearInputs()
        {
            base.ClearInputs();
            _lastRunFrame = null;
            _stepFrom = null;
            _stepHome = null;
            _stepFailures = 0;
            _mainMenuSinceFrame = null;
            _lastBattleFrame = null;
            _fishPhase = FishPhase.Idle;
            _fishFailedCasts = 0;
            _pathStepActive = false;
            _pathAPressed = false;
            _walkDirection = null;
            _startersReadyCalls = 0;
            _starterPicked = false;
            _rockFrames = 0;
            SetCirclePad(null);
        }

        #region Starter

        /// <summary>HGSS: calls the three generated starters must stay readable before reading them.</summary>
        private const int StartersStableCalls = 10;
        private int _startersReadyCalls;
        private bool _starterPicked;

        /// <summary>
        /// HGSS: the three starters are generated together when the choice scene opens (in front of the balls in Elm's
        /// lab), nothing has to be chosen: A advances the texts until they are readable.
        /// DPPt / Gen 5: advances the texts until the briefcase (gift box) lets the player choose, moves the cursor to
        /// the starter, then confirms. The starter is generated when it is confirmed (after the bot wrote a random RNG
        /// state).
        /// </summary>
        public override bool ExecuteStarter(int indexStarter)
        {
            if (Memory.Pointers.StarterData != 0 && Memory.Pointers.StarterSelected == 0)
            {
                if (Memory.GetGeneratedStarters().Count == 3)
                {
                    return ++_startersReadyCalls >= StartersStableCalls;
                }

                _startersReadyCalls = 0;
                if (!IsBusy)
                {
                    QueueButton("A", TextHoldFrames, TextReleaseFrames);
                }

                return false;
            }

            if (Memory.Pointers.StarterData != 0)
            {
                // Ultra Sun/Moon: the chosen starter is readable once generated, before it joins the party.
                // Omega Ruby/Alpha Sapphire: StarterData is the first party slot.
                if (Memory.GetGeneratedStarters().Count > 0)
                {
                    return ++_startersReadyCalls >= StartersStableCalls;
                }

                _startersReadyCalls = 0;
            }

            if (Memory.GetPartyCount() > 0)
            {
                return true;
            }

            if (IsBusy)
            {
                return false;
            }

            if (Memory.Pointers.StarterMenuVertical)
            {
                // Ultra Sun/Moon list (Rowlet / Litten / Popplio): once the starter is picked only A is pressed, the
                // cursor byte may be reused by the next menus (a direction there could answer NO)
                var cursor = _starterPicked ? indexStarter : Memory.GetStarterSelection();
                if (cursor < indexStarter && cursor >= 0)
                {
                    QueueButton("Down", MenuHoldFrames, MenuReleaseFrames);
                }
                else if (cursor > indexStarter)
                {
                    QueueButton("Up", MenuHoldFrames, MenuReleaseFrames);
                }
                else
                {
                    _starterPicked |= cursor == indexStarter;
                    QueueButton("A", TextHoldFrames, TextReleaseFrames);
                }

                return false;
            }

            var selection = Memory.GetStarterSelection();
            if (selection == -2)
            {
                // Gen 5 gift box shown without a highlighted ball: a direction highlights one
                QueueButton("Right", MenuHoldFrames, MenuReleaseFrames);
            }
            else if (selection < 0)
            {
                // Texts before the briefcase (and the "Will you choose...?" question, YES is the default)
                QueueButton("A", TextHoldFrames, TextReleaseFrames);
            }
            else if (selection < indexStarter)
            {
                QueueButton("Right", MenuHoldFrames, MenuReleaseFrames);
            }
            else if (selection > indexStarter)
            {
                QueueButton("Left", MenuHoldFrames, MenuReleaseFrames);
            }
            else
            {
                QueueButton("A", TextHoldFrames, TextReleaseFrames);
            }

            return false;
        }

        #endregion

        #region Spin / battle

        public override bool Spin()
        {
            if (IsBusy)
            {
                return true;
            }

            if (Memory.Pointers.AnalogMovement)
            {
                return RockWithCirclePad();
            }

            if (Memory.WalksForEncounters)
            {
                return StepBackAndForth();
            }

            // Skip the direction the player already faces, pressing it would walk instead of turning
            var facing = Memory.GetPlayer().FacingDirection;
            var direction = _spinOrder[_spinIndex % _spinOrder.Length];
            if (direction == facing)
            {
                _spinIndex++;
                direction = _spinOrder[_spinIndex % _spinOrder.Length];
            }

            _spinIndex++;
            QueueButton(DirectionButton(direction), TurnHoldFrames, TurnReleaseFrames);
            return true;
        }

        /// <summary>Ultra Sun/Moon: frames the Circle Pad is held each way (the same both ways, so the player doesn't drift).</summary>
        private const int RockFrames = 20;
        private const int CirclePadPush = 120;
        private int _rockFrames;
        private int _rockDirection = -1;

        /// <summary>
        /// Ultra Sun/Moon: walks left and right with the Circle Pad (the D-pad doesn't walk there, and there is no
        /// tile position to go back to).
        /// </summary>
        private bool RockWithCirclePad()
        {
            var sinceBattle = FramesSince(_lastBattleFrame);
            if (sinceBattle != null && sinceBattle < AfterBattleFrames)
            {
                SetCirclePad(null);
                return true;
            }

            if (++_rockFrames > RockFrames)
            {
                _rockFrames = 0;
                _rockDirection = -_rockDirection;
            }

            APIContainer.Joypad.SetAnalog("Circle Pad X", CirclePadPush * _rockDirection, null);
            return true;
        }

        private static readonly PlayerFacingDirection[] _stepAwayOrder =
        {
            PlayerFacingDirection.Left,
            PlayerFacingDirection.Right,
            PlayerFacingDirection.Up,
            PlayerFacingDirection.Down,
        };
        /// <summary>
        /// Steps away from home without moving before trying another direction. The presses are also ignored during
        /// the ~1.5-2 s before a battle starts (up to 4 of them in Black), which must not count as a wall.
        /// </summary>
        private const int StepAwayFailures = 6;
        private (int X, int Z)? _stepHome;
        private (uint X, uint Z)? _stepFrom;
        private int _stepAway;
        private int _stepFailures;

        /// <summary>
        /// Gen 5: walks one tile away from the start tile and back. Going back is always toward the start tile, so
        /// presses lost before a battle can't make the player drift out of the grass. A direction that doesn't move
        /// (wall, ledge...) is replaced by the next one.
        /// </summary>
        private bool StepBackAndForth()
        {
            // The player can't move while the screen fades in after a battle: presses then would count as blocked
            var sinceBattle = FramesSince(_lastBattleFrame);
            if (sinceBattle != null && sinceBattle < AfterBattleFrames)
            {
                return true;
            }

            var raw = Memory.GetRawPosition();
            if (raw.X == 0 && raw.Z == 0)
            {
                // Read as (0, 0) around battle transitions
                return true;
            }

            var tile = Memory.GetTilePosition();
            if (_stepHome == null)
            {
                _stepHome = tile;
            }

            var home = _stepHome.Value;
            PlayerFacingDirection direction;
            if (tile != home)
            {
                _stepFailures = 0;
                direction = tile.X != home.X
                    ? (tile.X > home.X ? PlayerFacingDirection.Left : PlayerFacingDirection.Right)
                    : (tile.Z > home.Z ? PlayerFacingDirection.Up : PlayerFacingDirection.Down);
            }
            else
            {
                // Back home: did the last step away move at all?
                if (_stepFrom != null && raw == _stepFrom.Value && ++_stepFailures >= StepAwayFailures)
                {
                    _stepAway = (_stepAway + 1) % _stepAwayOrder.Length;
                    _stepFailures = 0;
                    Log.Debug($"Spin: can't walk there, now stepping {_stepAwayOrder[_stepAway]} and back");
                }

                direction = _stepAwayOrder[_stepAway];
            }

            _stepFrom = raw;
            QueueButton(DirectionButton(direction), StepHoldFrames, StepReleaseFrames);
            return true;
        }

        public override bool Escape()
        {
            _lastBattleFrame = FrameCount;
            _fishPhase = FishPhase.Idle;
            _fishFailedCasts = 0;
            // The position after the battle says nothing about the last step
            _stepFrom = null;

            if (Memory.Pointers.AnalogMovement)
            {
                SetCirclePad(null);
            }

            if (IsBusy)
            {
                return false;
            }

            var frame = FrameCount;
            var runSequence = (IReadOnlyList<string>?)Memory.Pointers.RunSequence ?? _runSequence;
            if (Memory.Pointers.BattleMenuState == 0)
            {
                // Ultra Sun/Moon: no known menu state. B leaves a move list / skips a text, then the sequence moves to
                // RUN from any button; sent again until the battle is over (a press landing on texts does nothing)
                var blindReady = _lastRunFrame == null || frame - _lastRunFrame.Value >= FramesBetweenBlindRuns || frame < _lastRunFrame.Value;
                if (blindReady)
                {
                    _lastRunFrame = frame;
                    QueueButton("B", TextSkipHoldFrames, TextReleaseFrames);
                    foreach (var button in runSequence)
                    {
                        QueueButton(button, MenuHoldFrames, MenuReleaseFrames);
                    }
                }

                return false;
            }

            var menu = Memory.GetBattleMenuState();
            if (menu != _lastLoggedMenu)
            {
                Log.Debug($"Battle menu state: {_lastLoggedMenu} -> {menu} (frame {frame})");
                _lastLoggedMenu = menu;
            }

            if (menu != BattleMainMenu)
            {
                _mainMenuSinceFrame = null;
                if (Memory.Pointers.BattleSubMenus.Contains(menu))
                {
                    // A move list / bag / party opened (e.g. a press landed on FIGHT): go back
                    QueueButton("B", MenuHoldFrames, MenuReleaseFrames);
                }
                else
                {
                    // Battle texts and animations: B skips the text waits
                    QueueButton("B", TextSkipHoldFrames, TextSkipReleaseFrames);
                }

                return false;
            }

            if (_mainMenuSinceFrame == null || frame < _mainMenuSinceFrame)
            {
                _mainMenuSinceFrame = frame;
            }

            var menuReady = frame - _mainMenuSinceFrame.Value >= MainMenuSettleFrames;
            // No int.MinValue sentinel here: frame - int.MinValue overflows and the RUN sequence was never sent
            var retryReady = _lastRunFrame == null || frame - _lastRunFrame.Value >= FramesBetweenRunAttempts || frame < _lastRunFrame.Value;
            if (menuReady && retryReady)
            {
                Log.Debug($"Selecting RUN: {string.Join(" ", runSequence)} (frame {frame})");
                _lastRunFrame = frame;
                foreach (var button in runSequence)
                {
                    QueueButton(button, MenuHoldFrames, MenuReleaseFrames);
                }
            }

            return false;
        }

        #endregion

        #region Texts / gifts / static

        public override bool UseRegisteredItem()
        {
            if (!IsBusy)
            {
                // The registered key item is on Y
                QueueButton("Y", 3, 3);
            }

            return true;
        }

        public override bool Interact()
        {
            if (!IsBusy)
            {
                QueueButton("A", TextHoldFrames, TextReleaseFrames);
            }

            return true;
        }

        #endregion

        #region Fishing

        private enum FishPhase
        {
            Idle,
            Casting,
            Hooking,
        }

        private static readonly int[] _rods = { 445, 446, 447 }; // Old Rod, Good Rod, Super Rod (Gen 5 only has the Super Rod)

        /// <summary>
        /// Frames after the end of a battle before using the rod again: with 90 the screen was still fading in
        /// and the Y press was ignored.
        /// </summary>
        private const int AfterBattleFrames = 150;
        /// <summary>Frames of the cast animation, the bite flag isn't looked at before.</summary>
        private const int CastAnimationFrames = 60;
        /// <summary>
        /// Frames to wait for a bite / "Not even a nibble..." before giving up on a cast (the result came ~310 frames
        /// after Y in our Platinum tests).
        /// </summary>
        private const int CastTimeoutFrames = 600;
        /// <summary>
        /// HGSS: the flag stays 0 when nothing bites ("Not even a nibble..." starts ~305 frames after Y, a bite sets
        /// the flag ~313 frames after Y), so no bite after this many frames means that nothing bit.
        /// </summary>
        private const int NoBiteFrames = 360;
        /// <summary>The bite flag goes back to 0 right after Y when the rod is cast; if it doesn't, Y was ignored.</summary>
        private const int CastStartFrames = 120;
        /// <summary>Frames of B taps after hooking before considering that the Pokemon got away.</summary>
        private const int HookTimeoutFrames = 400;
        /// <summary>
        /// Casts in a row without any battle before stopping: 0.75^30 = 0.02% with the Old Rod (25% bite rate),
        /// so it means that the player can't fish here (not facing water, "There's a time and place...").
        /// </summary>
        private const int MaxFailedCasts = 30;

        private FishPhase _fishPhase;
        private int? _fishPhaseFrame;
        private bool _fishSawRest;
        private int _fishFailedCasts;

        /// <summary>
        /// Fishing (same flow as wyanido/pokebot-nds): Y to cast, wait for the bite flag, A as soon as
        /// something bites, then B through "Landed a Pokemon!" until the battle starts.
        /// B (not A) closes "Not even a nibble...": A in front of water asks to Surf.
        /// </summary>
        public override bool Fish()
        {
            if (IsBusy)
            {
                return true;
            }

            var sinceBattle = FramesSince(_lastBattleFrame);
            if (sinceBattle != null && sinceBattle < AfterBattleFrames)
            {
                return true;
            }

            var elapsed = FramesSince(_fishPhaseFrame);
            switch (_fishPhase)
            {
                case FishPhase.Idle:
                    // HGSS/BW can register several items: Y would then open a choice, so the rod has to be the only one
                    // (B2W2: the registered items aren't known, a cast that doesn't start counts as a failed cast)
                    if (Memory.CanReadRegisteredItems)
                    {
                        var registered = Memory.GetRegisteredItems();
                        if (registered.Count != 1 || !_rods.Contains(registered[0]))
                        {
                            throw new BotException(Messages.BotFishing_NoRod);
                        }
                    }

                    if (_fishFailedCasts >= MaxFailedCasts)
                    {
                        throw new BotException(string.Format(Messages.BotFishing_NothingCaught, _fishFailedCasts));
                    }

                    Log.Debug($"Fishing: cast (frame {FrameCount}, failed casts {_fishFailedCasts})");
                    QueueButton("Y", TextHoldFrames, 0);
                    SetFishPhase(FishPhase.Casting);
                    _fishSawRest = false;
                    break;

                case FishPhase.Casting:
                    if (elapsed == null)
                    {
                        SetFishPhase(FishPhase.Idle);
                        break;
                    }

                    var bite = Memory.GetFishingBite();
                    if (elapsed < (Memory.Pointers.FishingCastFrames ?? CastAnimationFrames))
                    {
                        // The flag is reset when the rod is cast: wait to see it at rest before reading a result
                        _fishSawRest |= bite == 0 && elapsed > 10;
                        break;
                    }

                    _fishSawRest |= bite == 0;
                    if (!_fishSawRest && elapsed > CastStartFrames)
                    {
                        // Y ignored (screen still fading in after a battle...) or a text box is open: close it, cast again
                        Log.Debug($"Fishing: the rod wasn't cast (flag {bite}), trying again");
                        _fishFailedCasts++;
                        QueueButton("B", TextHoldFrames, 20);
                        SetFishPhase(FishPhase.Idle);
                    }
                    else if (_fishSawRest && bite == 1)
                    {
                        Log.Debug($"Fishing: bite (frame {FrameCount})");
                        QueueButton("A", TextHoldFrames, 4);
                        SetFishPhase(FishPhase.Hooking);
                    }
                    else if (_fishSawRest && bite != 0)
                    {
                        Log.Debug($"Fishing: nothing bit (flag {bite}, frame {FrameCount})");
                        _fishFailedCasts++;
                        // First B ends the text printing, second closes it (the text opens a bit after the flag)
                        QueueWait(Memory.Pointers.FishingTextFrames ?? 20);
                        QueueButton("B", TextHoldFrames, 20);
                        QueueButton("B", TextHoldFrames, 20 + (Memory.Pointers.FishingRecastFrames ?? 0));
                        SetFishPhase(FishPhase.Idle);
                    }
                    else if (_fishSawRest && bite == 0 && elapsed > (Memory.Pointers.FishingNoBiteFrames ?? NoBiteFrames))
                    {
                        Log.Debug($"Fishing: nothing bit (no flag after {elapsed} frames, frame {FrameCount})");
                        _fishFailedCasts++;
                        QueueButton("B", TextHoldFrames, 20);
                        QueueButton("B", TextHoldFrames, 20);
                        SetFishPhase(FishPhase.Idle);
                    }
                    else if (elapsed > CastTimeoutFrames)
                    {
                        Log.Debug($"Fishing: no result after {elapsed} frames (flag {bite})");
                        _fishFailedCasts++;
                        QueueButton("B", TextHoldFrames, 20);
                        SetFishPhase(FishPhase.Idle);
                    }

                    break;

                case FishPhase.Hooking:
                    if (elapsed == null || elapsed > HookTimeoutFrames)
                    {
                        // The Pokemon got away: the battle didn't start
                        Log.Debug("Fishing: no battle after the bite");
                        _fishFailedCasts++;
                        SetFishPhase(FishPhase.Idle);
                        break;
                    }

                    // "Landed a Pokemon!"
                    QueueButton("B", TextSkipHoldFrames, TextReleaseFrames);
                    break;
            }

            return true;
        }

        private void SetFishPhase(FishPhase phase)
        {
            _fishPhase = phase;
            _fishPhaseFrame = FrameCount;
        }

        #endregion

        #region Path

        /// <summary>Frames holding a direction without moving before skipping the step (wall, NPC...).</summary>
        private const int PathBlockedFrames = 40;
        /// <summary>Position updates every 2 frames while walking: the step is over once it stays still this long.</summary>
        private const int PathStillFrames = 4;

        private bool _pathStepActive;
        private PathAction _pathAction;
        private (uint X, uint Z) _pathStart;
        private (int X, int Z) _pathStartTile;
        private (uint X, uint Z) _pathLast;
        private bool _pathMoved;
        private int _pathFrames;
        private int _pathStill;
        private bool _pathAPressed;

        /// <summary>
        /// One tile per direction: the direction is held until the player starts moving (turning first takes
        /// ~8 frames when facing elsewhere), then released so the walk stops on the next tile.
        /// </summary>
        public override bool PathStep(PathAction action)
        {
            if (IsBusy)
            {
                return false;
            }

            if (action == PathAction.A)
            {
                if (_pathAPressed)
                {
                    _pathAPressed = false;
                    return true;
                }

                QueueButton("A", TextHoldFrames, TextReleaseFrames);
                _pathAPressed = true;
                return false;
            }

            var position = Memory.GetRawPosition();
            if (!_pathStepActive || _pathAction != action)
            {
                _pathStepActive = true;
                _pathAction = action;
                _pathStart = position;
                _pathStartTile = Memory.GetTilePosition();
                _pathLast = position;
                _pathMoved = false;
                _pathFrames = 0;
                _pathStill = 0;
                Log.Debug($"Path: {action} from {position.X:X8},{position.Z:X8} facing {Memory.GetPlayer().FacingDirection} (frame {FrameCount})");
            }

            _pathFrames++;
            if (!_pathMoved)
            {
                // Circle Pad: the walk is continuous, the step is over once the player is on another tile
                var moved = Memory.Pointers.AnalogMovement ? Memory.GetTilePosition() != _pathStartTile : position != _pathStart;
                if (moved)
                {
                    _pathMoved = true;
                    _pathLast = position;
                    SetCirclePad(null);
                    return false;
                }

                if (_pathFrames > PathBlockedFrames)
                {
                    Log.Debug($"Path: {action} blocked, step skipped ({position.X:X8},{position.Z:X8}, frame {FrameCount})");
                    _pathStepActive = false;
                    SetCirclePad(null);
                    return true;
                }

                if (Memory.Pointers.AnalogMovement)
                {
                    SetCirclePad(PathDirection(action));
                }
                else
                {
                    APIContainer.Joypad.Set(PathButton(action), true);
                }

                return false;
            }

            if (position == _pathLast)
            {
                if (++_pathStill >= PathStillFrames)
                {
                    Log.Debug($"Path: {action} done at {position.X:X8},{position.Z:X8} after {_pathFrames} frames");
                    _pathStepActive = false;
                    return true;
                }
            }
            else
            {
                _pathLast = position;
                _pathStill = 0;
            }

            return false;
        }

        private static PlayerFacingDirection PathDirection(PathAction action)
        {
            switch (action)
            {
                case PathAction.Up:
                    return PlayerFacingDirection.Up;
                case PathAction.Down:
                    return PlayerFacingDirection.Down;
                case PathAction.Left:
                    return PlayerFacingDirection.Left;
                default:
                    return PlayerFacingDirection.Right;
            }
        }

        /// <summary>
        /// Ultra Sun/Moon: pushes the Circle Pad toward <paramref name="direction"/> for the next frame, or releases
        /// it (null). Down is a negative Y.
        /// </summary>
        private void SetCirclePad(PlayerFacingDirection? direction)
        {
            if (!Memory.Pointers.AnalogMovement)
            {
                return;
            }

            int? x = null, y = null;
            switch (direction)
            {
                case PlayerFacingDirection.Left:
                    x = -CirclePadPush;
                    break;
                case PlayerFacingDirection.Right:
                    x = CirclePadPush;
                    break;
                case PlayerFacingDirection.Up:
                    y = CirclePadPush;
                    break;
                case PlayerFacingDirection.Down:
                    y = -CirclePadPush;
                    break;
            }

            APIContainer.Joypad.SetAnalog("Circle Pad X", x, null);
            APIContainer.Joypad.SetAnalog("Circle Pad Y", y, null);
        }

        private static string PathButton(PathAction action)
        {
            switch (action)
            {
                case PathAction.Up:
                    return "Up";
                case PathAction.Down:
                    return "Down";
                case PathAction.Left:
                    return "Left";
                default:
                    return "Right";
            }
        }

        #endregion

        #region Egg hatching

        /// <summary>Frames without moving before turning around.</summary>
        private const int WalkBlockedFrames = 40;
        /// <summary>
        /// While the player doesn't move (hatching texts, nickname question...) B is tapped every this many
        /// frames: it advances the texts and answers NO to the nickname question.
        /// </summary>
        private const int WalkTextTapFrames = 16;

        private PlayerFacingDirection? _walkDirection;
        private (uint X, uint Z) _walkLast;
        private int _walkStill;
        private int? _lastGearTapFrame;
        private const int GearTapFrames = 120;

        /// <summary>
        /// Runs (B held) or rides back and forth, turning around when blocked. When the player stops moving
        /// (the egg hatches, a text box...), B taps advance the texts.
        /// </summary>
        public override bool HatchEggStep(int blockedFrames = WalkBlockedFrames)
        {
            if (IsBusy)
            {
                return true;
            }

            var position = Memory.GetRawPosition();
            if (_walkDirection == null)
            {
                // Ultra Sun/Moon: no facing byte, start to the left
                _walkDirection = Memory.Pointers.AnalogMovement ? PlayerFacingDirection.Left : Memory.GetPlayer().FacingDirection;
                _walkLast = position;
                _walkStill = 0;
            }

            if (position != _walkLast)
            {
                _walkLast = position;
                _walkStill = 0;
            }
            else
            {
                _walkStill++;
            }

            if (_walkStill > 0 && _walkStill % blockedFrames == 0)
            {
                _walkDirection = OppositeDirection(_walkDirection.Value);
                Log.Debug($"Egg walk: turning around ({_walkDirection})");
            }

            var onBike = Memory.IsOnBike();
            if (_walkStill > 0 && _walkStill % WalkTextTapFrames == 0)
            {
                SetCirclePad(null);
                QueueButton("B", TextSkipHoldFrames, TextSkipReleaseFrames);
                return true;
            }

            if (Memory.Pointers.AnalogMovement)
            {
                SetCirclePad(_walkDirection.Value);
            }
            else
            {
                APIContainer.Joypad.Set(DirectionButton(_walkDirection.Value), true);
            }
            if (!onBike && _walkStill < 4)
            {
                // Running shoes; released while stopped so the B taps above are new presses
                APIContainer.Joypad.Set("B", true);
            }
            else if (onBike && _walkStill == 0 && !Memory.IsBikeFastGear() && (FramesSince(_lastGearTapFrame) ?? int.MaxValue) > GearTapFrames)
            {
                // B switches the bike gear (and a B tap above may have set the slow one); not too often in case
                // the gear value isn't the one expected
                _lastGearTapFrame = FrameCount;
                QueueButton("B", TextSkipHoldFrames, TextSkipReleaseFrames);
            }

            return true;
        }

        #endregion
    }
}
