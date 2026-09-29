using System;

namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// Deterministic value noise and fBm, pure and dependency-free.
    ///
    /// The hash is a pure function of the lattice coordinate, so the same world
    /// comes out in the Editor and in the browser, and matches the original's
    /// bit for bit: every step is 32-bit unsigned arithmetic, which is exact.
    /// </summary>
    public static class Noise
    {
        private static double Hash2(int ix, int iy)
        {
            unchecked
            {
                uint h = (uint)ix * 0x27d4eb2du ^ (uint)iy * 0x165667b1u;
                h = (h ^ (h >> 15)) * 0x2c1b3c6du;
                h = (h ^ (h >> 12)) * 0x297a2d39u;
                h ^= h >> 15;
                return h / 4294967296.0;
            }
        }

        private static double Smooth(double t) => t * t * t * (t * (t * 6 - 15) + 10);

        /// <summary>Value noise in [-1, 1].</summary>
        public static double Value2D(double x, double y)
        {
            double fx0 = Math.Floor(x), fy0 = Math.Floor(y);
            int ix = (int)fx0, iy = (int)fy0;
            double fx = x - fx0, fy = y - fy0;
            double u = Smooth(fx), v = Smooth(fy);
            double a = Hash2(ix, iy);
            double b = Hash2(ix + 1, iy);
            double c = Hash2(ix, iy + 1);
            double d = Hash2(ix + 1, iy + 1);
            double top = a + (b - a) * u;
            double bot = c + (d - c) * u;
            return (top + (bot - top) * v) * 2 - 1;
        }

        public static double Fbm2D(double x, double y, int octaves = 4, double lacunarity = 2, double gain = 0.5)
        {
            double sum = 0, amp = 1, norm = 0, fx = x, fy = y;
            for (int i = 0; i < octaves; i++)
            {
                sum += Value2D(fx, fy) * amp;
                norm += amp;
                amp *= gain;
                fx *= lacunarity;
                fy *= lacunarity;
            }
            return norm == 0 ? 0 : sum / norm;
        }
    }
}
