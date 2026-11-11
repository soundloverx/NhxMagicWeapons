using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace NhxMagicWeapons
{
    // Recoloured copies of the bundle's textures and icons, made by Tools\recolor_armor.py (the Firecaller's Regalia's red)
    // or Tools\recolor_flames.py (the Wrath of Muspelheim's blue flames) and embedded in the DLL. The file's format is
    // described in recolor_armor.py
    internal sealed class RecoloredTextures
    {
        private const string Magic = "NHXR";
        private const int Version = 1;

        // By the original texture's name (or another key of the tool's)
        internal readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();
        // By the original sprite's name
        internal readonly Dictionary<string, Sprite> Icons = new Dictionary<string, Sprite>();

        // Null on a dedicated server, which draws nothing, or when the file is missing or unreadable, after a warning
        // ending with `without`, what that means. Each texture and icon is named after its key and `suffix`
        internal static RecoloredTextures Load(string resource, string suffix, string without)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                return null;
            }
            Stream stream = typeof(Plugin).Assembly.GetManifestResourceStream(resource);
            if (stream == null)
            {
                Plugin.Log.LogWarning($"{resource} isn't in the DLL: {without}.");
                return null;
            }
            RecoloredTextures loaded = new RecoloredTextures();
            using (BinaryReader reader = new BinaryReader(new DeflateStream(stream, CompressionMode.Decompress)))
            {
                if (Encoding.ASCII.GetString(reader.ReadBytes(Magic.Length)) != Magic || reader.ReadUInt16() != Version)
                {
                    Plugin.Log.LogWarning($"{resource} isn't a file of Tools\\recolor_armor.py's or recolor_flames.py's: {without}.");
                    return null;
                }
                int count = reader.ReadUInt16();
                for (int i = 0; i < count; i++)
                {
                    string key = Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadUInt16()));
                    bool icon = reader.ReadByte() == 1;
                    TextureFormat format = (TextureFormat)reader.ReadInt32();
                    int width = reader.ReadUInt16();
                    int height = reader.ReadUInt16();
                    int mips = reader.ReadByte();
                    Vector2 pivot = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    float pixelsPerUnit = reader.ReadSingle();
                    byte[] data = reader.ReadBytes((int)reader.ReadUInt32());

                    // Compressed as the GPU takes it, with its mipmaps; then the copy in memory isn't needed
                    Texture2D texture = new Texture2D(width, height, format, mips, false) { name = key.Replace('/', '_') + suffix };
                    texture.LoadRawTextureData(data);
                    texture.Apply(false, true);
                    if (icon)
                    {
                        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, width, height), pivot, pixelsPerUnit, 0, SpriteMeshType.FullRect);
                        sprite.name = key + suffix;
                        loaded.Icons[key] = sprite;
                    }
                    else
                    {
                        loaded.Textures[key] = texture;
                    }
                }
            }
            return loaded;
        }

        // A recoloured texture, sampled as the original
        internal static Texture2D Matching(Texture2D texture, Texture original)
        {
            if (original)
            {
                texture.wrapMode = original.wrapMode;
                texture.filterMode = original.filterMode;
                texture.anisoLevel = original.anisoLevel;
            }
            return texture;
        }
    }
}
