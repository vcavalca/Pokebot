using BizHawk.Client.Common;
using Pokebot.Models.Memory;
using Pokebot.Models.Player;
using Pokebot.Utils;
using System.Collections.Generic;

namespace Pokebot.Models.ActionRunners
{
    public abstract class CommonActionRunner : IActionRunner
    {
        public ApiContainer APIContainer { get; }
        public IGameMemory GameVersion { get; }
        protected Dictionary<PlayerFacingDirection, string> NextDirections { get; private set; }

        public CommonActionRunner(ApiContainer apiContainer, IGameMemory gameVersion)
        {
            APIContainer = apiContainer;
            GameVersion = gameVersion;
            NextDirections = new Dictionary<PlayerFacingDirection, string>()
            {
                { PlayerFacingDirection.Up, "Right" },
                { PlayerFacingDirection.Right, "Down" },
                { PlayerFacingDirection.Down, "Left" },
                { PlayerFacingDirection.Left, "Up" },
            };
        }

        public abstract bool ExecuteStarter(int indexStarter);

        public virtual bool Spin()
        {
            var player = GameVersion.GetPlayer();
            if (NextDirections.ContainsKey(player.FacingDirection))
            {
                APIContainer.Joypad.Set(NextDirections[player.FacingDirection], true);

                return true;
            }

            return false;
        }

        public virtual bool Escape()
        {
            var state = GameVersion.GetGameState();
            if (state == GameState.Battle)
            {
                GameVersion.TrySetEscape();

                var action = (BattleActionSelectionCursor)GameVersion.GetActionSelectionCursor();

                switch (action)
                {
                    case BattleActionSelectionCursor.Moves:
                        PressB();
                        PressRight();
                        return false;
                    case BattleActionSelectionCursor.Bag:
                        PressDown();
                        return false;
                    case BattleActionSelectionCursor.Team:
                        PressUp();
                        return false;
                    case BattleActionSelectionCursor.Escape:
                        PressA();
                        return false;
                }
            }

            return false;
        }

        public virtual bool UseRegisteredItem()
        {
            PressSelect();

            return true;
        }

        public virtual bool ReceivePokemon()
        {
            return Interact();
        }

        public virtual bool Interact()
        {
            APIContainer.Joypad.Set("A", true);

            return true;
        }

        public virtual bool Fish()
        {
            throw new System.NotSupportedException("Fishing is not supported for this game");
        }

        public virtual bool PathStep(PathAction action)
        {
            throw new System.NotSupportedException("The path bot is not supported for this game");
        }

        // Egg hatching walk: calls without moving before turning around (wall, NPC...)
        private const int HatchBlockedCallsBeforeReverse = 40;

        private PlayerFacingDirection? _hatchDirection;
        private Position? _hatchLastPosition;
        private int _hatchStillCalls;
        private int _hatchCalls;

        /// <summary>
        /// Walks forward until something blocks the way, then walks back the other way, and taps B
        /// to advance the hatching texts and answer NO to the nickname question.
        /// </summary>
        public virtual bool HatchEggStep(int blockedFrames = HatchBlockedCallsBeforeReverse)
        {
            var player = GameVersion.GetPlayer();
            _hatchCalls++;

            if (_hatchDirection == null || _hatchLastPosition == null || Distance(player.Position, _hatchLastPosition) > 2)
            {
                // (Re)start towards where the player is facing (also after loading a save state)
                _hatchDirection = player.FacingDirection;
                _hatchLastPosition = player.Position;
                _hatchStillCalls = 0;
            }
            else if (player.Position != _hatchLastPosition)
            {
                _hatchLastPosition = player.Position;
                _hatchStillCalls = 0;
            }
            else if (++_hatchStillCalls > blockedFrames)
            {
                _hatchDirection = OppositeDirection(_hatchDirection.Value);
                _hatchStillCalls = 0;
            }

            APIContainer.Joypad.Set(DirectionButton(_hatchDirection.Value), true);
            if (_hatchCalls % 2 == 0)
            {
                PressB();
            }

            return true;
        }

        protected static int Distance(Position a, Position b)
        {
            return System.Math.Abs(a.X - b.X) + System.Math.Abs(a.Y - b.Y);
        }

        protected static PlayerFacingDirection OppositeDirection(PlayerFacingDirection direction)
        {
            switch (direction)
            {
                case PlayerFacingDirection.Up:
                    return PlayerFacingDirection.Down;
                case PlayerFacingDirection.Down:
                    return PlayerFacingDirection.Up;
                case PlayerFacingDirection.Left:
                    return PlayerFacingDirection.Right;
                default:
                    return PlayerFacingDirection.Left;
            }
        }

        protected static string DirectionButton(PlayerFacingDirection direction)
        {
            switch (direction)
            {
                case PlayerFacingDirection.Up:
                    return "Up";
                case PlayerFacingDirection.Down:
                    return "Down";
                case PlayerFacingDirection.Left:
                    return "Left";
                default:
                    return "Right";
            }
        }

        public void PressA() => APIContainer.Joypad.SetWhenInactive("A");
        public void PressB() => APIContainer.Joypad.SetWhenInactive("B");
        public void PressX() => APIContainer.Joypad.SetWhenInactive("X");
        public void PressY() => APIContainer.Joypad.SetWhenInactive("Y");
        public void PressLeft() => APIContainer.Joypad.SetWhenInactive("Left");
        public void PressRight() => APIContainer.Joypad.SetWhenInactive("Right");
        public void PressDown() => APIContainer.Joypad.SetWhenInactive("Down");
        public void PressUp() => APIContainer.Joypad.SetWhenInactive("Up");
        public void PressStart() => APIContainer.Joypad.SetWhenInactive("Start");
        public void PressSelect() => APIContainer.Joypad.SetWhenInactive("Select");

        #region Timed inputs

        // Touch screen taps are not queued on purpose: in our BizHawk 2.10 tests the "Touch" button override
        // worked but the "Touch X/Y" coordinates set from scripts were ignored (mouse/neutral values were used).

        /// <summary>
        /// One step of a queued input: the button to hold (or null to wait) for <see cref="Frames"/> frames.
        /// </summary>
        private sealed class TimedInput
        {
            public string? Button { get; set; }
            public int Frames { get; set; }
        }

        private readonly Queue<TimedInput> _inputs = new Queue<TimedInput>();

        /// <summary>True while queued inputs are still being applied.</summary>
        public bool IsBusy => _inputs.Count > 0;

        public virtual void Update()
        {
            if (_inputs.Count == 0)
            {
                return;
            }

            var input = _inputs.Peek();
            if (input.Button != null)
            {
                APIContainer.Joypad.Set(input.Button, true);
            }

            input.Frames--;
            if (input.Frames <= 0)
            {
                _inputs.Dequeue();
            }
        }

        public virtual void ClearInputs()
        {
            _inputs.Clear();
        }

        /// <summary>
        /// Holds a button for <paramref name="holdFrames"/> frames, then waits <paramref name="releaseFrames"/> frames.
        /// </summary>
        protected void QueueButton(string button, int holdFrames, int releaseFrames)
        {
            _inputs.Enqueue(new TimedInput { Button = button, Frames = holdFrames });
            if (releaseFrames > 0)
            {
                _inputs.Enqueue(new TimedInput { Frames = releaseFrames });
            }
        }

        /// <summary>
        /// Waits <paramref name="frames"/> frames before the next queued input.
        /// </summary>
        protected void QueueWait(int frames)
        {
            if (frames > 0)
            {
                _inputs.Enqueue(new TimedInput { Frames = frames });
            }
        }

        #endregion
    }
}
