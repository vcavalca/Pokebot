namespace Pokebot.Models.ActionRunners
{
    public interface IActionRunner
    {
        /// <summary>
        /// Called once per emulated frame (before the bot runs) to keep applying inputs that last several frames.
        /// DS games read most inputs on alternate frames, so a 1 frame press is not reliable there.
        /// </summary>
        void Update();
        /// <summary>
        /// Drops the inputs that are still queued and resets the multi-frame routines (bot stopped, state loaded).
        /// </summary>
        void ClearInputs();
        /// <summary>
        /// Advances the texts until the starter at <paramref name="indexStarter"/> is chosen.
        /// Returns true once the starter is in the party.
        /// </summary>
        bool ExecuteStarter(int indexStarter);
        bool Spin();
        bool Escape();
        bool UseRegisteredItem();
        /// <summary>
        /// Advances the dialog that gives a Pokémon to the player (gift, fossil...).
        /// </summary>
        bool ReceivePokemon();
        /// <summary>
        /// Talks to / interacts with what is in front of the player (static Pokemon, texts).
        /// </summary>
        bool Interact();
        /// <summary>
        /// Uses the registered rod and reels in when something bites. Called while not in battle.
        /// Throws a BotException when fishing isn't possible (no rod registered, not facing water...).
        /// </summary>
        bool Fish();
        /// <summary>
        /// Executes one step of a user path. Returns true once the step is done.
        /// </summary>
        bool PathStep(PathAction action);
        /// <summary>
        /// Walks back and forth to hatch eggs, advancing the hatching texts. Turns around after
        /// <paramref name="blockedFrames"/> frames without moving.
        /// </summary>
        bool HatchEggStep(int blockedFrames = 40);
    }
}
