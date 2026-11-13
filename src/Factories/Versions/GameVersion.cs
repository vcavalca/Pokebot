using BizHawk.Client.Common;
using Pokebot.Models.ActionRunners;
using Pokebot.Models.Config;
using Pokebot.Models.Memory;

namespace Pokebot.Factories.Versions
{
    public class GameVersion
    {
        public ApiContainer APIContainer { get; }
        public VersionInfo VersionInfo { get; }
        public HashData HashData { get; }
        public GenerationInfo GenerationInfo { get; }
        public IGameMemory Memory { get; }
        public IActionRunner Runner { get; }

        /// <summary>
        /// DS and 3DS games (Gen 4-7, <see cref="NdsMemory"/> / <see cref="ThreeDsMemory"/>). Their runner paces the
        /// inputs by frames (the bots run every frame, no wall clock delay) and their encounters are told apart by PID.
        /// </summary>
        public bool IsDsGame => Memory is NdsMemory;

        public GameVersion(ApiContainer apiContainer, VersionInfo versionInfo, HashData hashData, GenerationInfo generationInfo, IGameMemory memory, IActionRunner runner)
        {
            APIContainer = apiContainer;
            VersionInfo = versionInfo;
            HashData = hashData;
            GenerationInfo = generationInfo;
            Memory = memory;
            Runner = runner;
        }
    }
}
