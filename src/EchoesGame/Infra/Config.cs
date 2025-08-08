using System;

namespace EchoesGame.Infra
{
    public static class Config
    {
        public const int WorldWidth = 4096;
        public const int WorldHeight = 4096;

        public static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
