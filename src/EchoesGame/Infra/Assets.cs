using System;
using System.Collections.Generic;
using System.IO;
using Raylib_cs;

namespace EchoesGame.Infra
{
    public static class Assets
    {
        private static readonly Dictionary<string, Texture2D> _textures = new();
        private static string _basePath = string.Empty;

        public static void Init(string basePath)
        {
            _basePath = basePath;
        }

        public static bool TryGet(string fileName, out Texture2D texture)
        {
            if (_textures.TryGetValue(fileName, out texture)) return true;
            try
            {
                string path = Path.Combine(_basePath, fileName);
                if (!File.Exists(path))
                {
                    texture = default;
                    return false;
                }
                Image img = Raylib.LoadImage(path);
                texture = Raylib.LoadTextureFromImage(img);
                Raylib.UnloadImage(img);
                _textures[fileName] = texture;
                return true;
            }
            catch
            {
                texture = default;
                return false;
            }
        }

        public static void Dispose()
        {
            foreach (var kv in _textures)
            {
                if (kv.Value.Id != 0)
                {
                    Raylib.UnloadTexture(kv.Value);
                }
            }
            _textures.Clear();
        }
    }
}


