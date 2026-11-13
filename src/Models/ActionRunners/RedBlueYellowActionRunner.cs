using BizHawk.Client.Common;
using Pokebot.Exceptions;
using Pokebot.Models.Memory;
using Pokebot.Models.Player;

namespace Pokebot.Models.ActionRunners
{
    internal class RedBlueYellowActionRunner : CommonActionRunner
    {
        // Spin calls without moving before trying the other side (wall, NPC...)
        private const int SpinBlockedCallsBeforeReverse = 30;

        private readonly Gen1Memory _gen1Memory;

        private PlayerFacingDirection? _spinDirection;
        private Position? _spinFrom;
        private int _spinBlockedCalls;

        public RedBlueYellowActionRunner(ApiContainer apiContainer, Gen1Memory gameVersion) : base(apiContainer, gameVersion)
        {
            _gen1Memory = gameVersion;
        }

        /// <summary>
        /// Gen 1 can't spin in place to find wild Pokemon: encounters only happen after a full step, and the
        /// first frame of a new direction only turns the player (180 degree turn check in OverworldLoop).
        /// Walks one step towards where the player faces, then back, holding the direction until the step starts.
        /// </summary>
        public override bool Spin()
        {
            var player = GameVersion.GetPlayer();
            if (player.RunningState == PlayerRunningState.Moving)
            {
                // A step is in progress, the game ignores input until it ends
                return true;
            }

            if (_spinDirection == null || _spinFrom == null || Distance(player.Position, _spinFrom) > 1)
            {
                // (Re)start: first step towards where the player is facing
                _spinDirection = player.FacingDirection;
                _spinFrom = player.Position;
                _spinBlockedCalls = 0;
            }
            else if (player.Position != _spinFrom)
            {
                // The step is done: walk back to the previous square
                _spinDirection = OppositeDirection(_spinDirection.Value);
                _spinFrom = player.Position;
                _spinBlockedCalls = 0;
            }
            else if (++_spinBlockedCalls > SpinBlockedCallsBeforeReverse)
            {
                // Blocked: try the other side
                _spinDirection = OppositeDirection(_spinDirection.Value);
                _spinBlockedCalls = 0;
            }

            APIContainer.Joypad.Set(DirectionButton(_spinDirection.Value), true);
            return true;
        }

        public override bool ExecuteStarter(int indexStarter)
        {
            var starterBotConfig = GameVersion.VersionInfo.BotsConfig.Starter;
            if (starterBotConfig == null)
            {
                throw new BotException(Messages.BotFactory_NotSupported);
            }

            var state = GameVersion.GetGameState();
            if (state != GameState.Overworld)
            {
                return false;
            }

            // After "<PLAYER> received <MON>!", the game asks for a nickname before generating the DVs:
            // answer NO (B), the starter is only readable once the prompt is closed.
            if (_gen1Memory.IsAddingPartyPokemon())
            {
                PressB();
                return false;
            }

            if (GameVersion.GetPartyCount() > 0)
            {
                return true;
            }

            var player = GameVersion.GetPlayer();
            // Coordinates are only updated at the end of a step, wait for it to avoid walking past the ball
            if (player.RunningState == PlayerRunningState.Moving)
            {
                return false;
            }

            var playerIndex = (player.Position.X - starterBotConfig.Position.X) + 1; //0 = left, 1 = center, 2 = right
            if (playerIndex == indexStarter)
            {
                if (player.FacingDirection != starterBotConfig.Facing)
                {
                    PressUp();
                }
                else
                {
                    PressA();
                }
            }
            else if (playerIndex > indexStarter)
            {
                PressLeft();
            }
            else if (playerIndex < indexStarter)
            {
                PressRight();
            }

            return false;
        }

        public override bool ReceivePokemon()
        {
            // The nickname prompt is shown before the DVs are generated: answer NO (B),
            // pressing A would open the naming screen.
            if (_gen1Memory.IsAddingPartyPokemon())
            {
                PressB();
                return true;
            }

            // Holding A gets stuck on Gen 1 screens that wait for a new press (Pokédex entry, YES/NO): tap it instead
            PressA();
            return true;
        }

        public override bool Escape()
        {
            var state = GameVersion.GetGameState();
            if (state != GameState.Battle)
            {
                return false;
            }

            GameVersion.TrySetEscape();

            // Gen 1 battle menu: FIGHT | PKMN
            //                    ITEM  | RUN
            var action = (BattleActionSelectionCursor)GameVersion.GetActionSelectionCursor();
            switch (action)
            {
                case BattleActionSelectionCursor.Moves:
                    PressDown();
                    break;
                case BattleActionSelectionCursor.Bag:
                    PressRight();
                    break;
                case BattleActionSelectionCursor.Team:
                    PressDown();
                    break;
                case BattleActionSelectionCursor.Escape:
                    PressA();
                    break;
                default:
                    // Not in the battle main menu (text box, move list...): B advances text and closes sub menus
                    PressB();
                    break;
            }

            return false;
        }
    }
}
