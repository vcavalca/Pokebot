using BizHawk.Client.Common;
using Pokebot.Exceptions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Pokebot.Utils
{
    public static class Extension
    {
        public static uint ToUInt32(this IEnumerable<byte> bytes)
        {
            return ToUInt32(bytes.ToArray());
        }

        public static int ToInt16(this IEnumerable<byte> bytes)
        {
            return ToInt16(bytes.ToArray());
        }

        public static int ToUInt16(this IEnumerable<byte> bytes)
        {
            return ToUInt16(bytes.ToArray());
        }

        public static uint ToUInt32(this byte[] bytes)
        {
            return BitConverter.ToUInt32(bytes, 0);
        }

        public static int ToInt16(this byte[] bytes)
        {
            return BitConverter.ToInt16(bytes, 0);
        }

        public static int ToUInt16(this byte[] bytes)
        {
            return BitConverter.ToUInt16(bytes, 0) & 0xFFFF;
        }

        public static int ToBE16(this IEnumerable<byte> bytesList)
        {
            var bytes = bytesList.ToArray();
            if (bytes.Length < 2)
            {
                throw new ArgumentException("Array must be at least 2 bytes long.");
            }
            return (bytes[0] << 8) | bytes[1];
        }

        public static int toBE24(this IEnumerable<byte> bytesList)
        {
            var bytes = bytesList.ToArray();
            if (bytes.Length < 3)
            {
                throw new ArgumentException("Array must be at least 3 bytes long.");
            }
            return (bytes[0] << 16) | (bytes[1] << 8) | bytes[2];
        }

        /// <summary>
        /// Checks whether <see cref="IEmuClientApi.SaveState(string)"/> already wrote a state with this name.
        /// Mirrors how BizHawk resolves the folder (per loaded system, e.g. GBA/State or Gameboy/State),
        /// using BizHawk's default path entries since the user's path config is not exposed to external tools.
        /// </summary>
        public static bool HasSaveState(this ApiContainer apiContainer, string name)
        {
            var systemId = apiContainer.Emulation.GetSystemId();
            var directory = new PathEntryCollection().SaveStateAbsolutePath(systemId);
            var fullPath = Path.Combine(directory, $"{name}.State");
            return File.Exists(fullPath);
        }

        public static bool LoadOrStop(this IEmuClientApi emuClient, string name)
        {
            bool loaded = false;
            try
            {
                loaded = emuClient.LoadState(name);
            }
            catch (FileNotFoundException) //If the save state doesn't exists
            {

            }
            finally
            {
                if (!loaded)
                {
                    throw new BotException(Messages.BotPokeFinder_InvalidSaveState);
                }
            }

            return loaded;
        }

        public static void SetWhenInactive(this IJoypadApi api, string button)
        {
            var buttonState = api.Get().FirstOrDefault(x => x.Key == button);
            if (!(bool)buttonState.Value)
            {
                api.Set(button, true);
            }
        }
    }
}
