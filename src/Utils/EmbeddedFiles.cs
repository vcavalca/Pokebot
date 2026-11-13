using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Pokebot.Utils
{
    /// <summary>
    /// Reads the plain files embedded in the DLL (configs, symbol files and sprites). See the EmbeddedResource items in the csproj.
    /// </summary>
    public static class EmbeddedFiles
    {
        private static readonly Assembly _assembly = typeof(EmbeddedFiles).Assembly;
        private static readonly Dictionary<string, Image?> _images = new Dictionary<string, Image?>();

        public static string ReadText(string resourceName)
        {
            using var stream = _assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                throw new FileNotFoundException($"Embedded file not found: {resourceName}");
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        public static IReadOnlyList<string> List(string prefix)
        {
            return _assembly.GetManifestResourceNames().Where(x => x.StartsWith(prefix, StringComparison.Ordinal)).OrderBy(x => x).ToList();
        }

        /// <summary>
        /// Gen 1-3: a pret symbol file by name (file name without extension, e.g. "pokeemerald" or "patchemerald_de"),
        /// or null if there is none.
        /// </summary>
        public static byte[]? GetSymbolFile(string name)
        {
            using var stream = _assembly.GetManifestResourceStream($"Pokebot.Symbols.{name}.sym");
            if (stream == null)
            {
                return null;
            }

            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }

        /// <summary>
        /// Returns the sprite of a species by national dex number, or null if there is none.
        /// </summary>
        public static Image? GetPokemonImage(int dexId, bool shiny)
        {
            var name = $"Pokebot.Images.{(shiny ? "shiny" : "normal")}.{dexId:000}.png";
            if (_images.TryGetValue(name, out var cached))
            {
                return cached;
            }

            Image? image = null;
            using (var stream = _assembly.GetManifestResourceStream(name))
            {
                if (stream != null)
                {
                    // Copy so the image doesn't depend on the stream staying open
                    using var temp = Image.FromStream(stream);
                    image = new Bitmap(temp);
                }
            }

            _images[name] = image;
            return image;
        }
    }
}
