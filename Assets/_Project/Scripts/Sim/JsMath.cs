using System;

namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// The handful of maths functions whose exact bits the simulation depends
    /// on, written out so that they give the same answer everywhere.
    ///
    /// Two reasons, and the second is the one that matters in the long run:
    ///
    /// <list type="number">
    /// <item><b>Parity with the TypeScript original.</b> The balance numbers in
    /// <see cref="Tune"/> were measured over 48 seeds there. They mean
    /// something here only if a seed produces the same match, bit for bit, and
    /// that requires the same arithmetic — including V8's own algorithm for
    /// <c>Math.hypot</c>, which is not <c>sqrt(x*x + y*y)</c>.</item>
    /// <item><b>Editor and browser must agree.</b> +, -, *, / and sqrt are
    /// exact IEEE 754 everywhere. Sine and cosine are not: the Editor's Mono
    /// calls the platform libm, a WebGL build calls musl compiled to wasm,
    /// and they may disagree in the last bit — which moves a shell burst by a
    /// hair, which flips a lethal roll, which is a different match. So the
    /// simulation never calls <see cref="Math.Sin"/> or <see cref="Math.Cos"/>;
    /// it calls fdlibm's, ported here, which is also exactly what V8 uses.</item>
    /// </list>
    ///
    /// Anything in <c>Sim/</c> that needs a transcendental function adds it
    /// here, ported, rather than reaching for <see cref="Math"/>.
    /// </summary>
    public static class JsMath
    {
        /// <summary>
        /// <c>Math.hypot(a, b)</c> exactly as V8 computes it: normalise by the
        /// larger magnitude, Kahan-sum the squares, scale back.
        /// </summary>
        public static double Hypot(double a, double b)
        {
            double x = Math.Abs(a), y = Math.Abs(b);
            if (double.IsPositiveInfinity(x) || double.IsPositiveInfinity(y)) return double.PositiveInfinity;
            if (double.IsNaN(x) || double.IsNaN(y)) return double.NaN;
            double max = 0;
            if (x > max) max = x;
            if (y > max) max = y;
            if (max == 0) return 0;

            double sum = 0, compensation = 0;
            double n = x / max;
            double summand = n * n - compensation;
            double preliminary = sum + summand;
            compensation = (preliminary - sum) - summand;
            sum = preliminary;

            n = y / max;
            summand = n * n - compensation;
            preliminary = sum + summand;
            compensation = (preliminary - sum) - summand;
            sum = preliminary;

            return Math.Sqrt(sum) * max;
        }

        /// <summary>
        /// <c>Math.round</c>: halves go toward +infinity. C#'s <see cref="Math.Round(double)"/>
        /// rounds halves to even, so 2.5 becomes 2 where JavaScript says 3.
        /// </summary>
        public static double Round(double x)
        {
            double r = Math.Floor(x);
            return x - r >= 0.5 ? r + 1 : r;
        }

        // --- fdlibm sin and cos ------------------------------------------------
        //
        // Ported from fdlibm 5.3 (k_sin.c, k_cos.c, s_sin.c, s_cos.c,
        // e_rem_pio2.c), which V8 ships as src/base/ieee754.cc.
        //
        //   Copyright (C) 1993 by Sun Microsystems, Inc. All rights reserved.
        //   Developed at SunSoft, a Sun Microsystems, Inc. business.
        //   Permission to use, copy, modify, and distribute this software is
        //   freely granted, provided that this notice is preserved.
        //
        // Argument reduction covers |x| < 2^20 * pi/2 (about 1.6 million) with
        // fdlibm's medium path. The simulation only ever passes angles in
        // [0, 2*pi); anything past the medium range throws rather than
        // quietly losing precision.

        private const double S1 = -1.66666666666666324348e-01;
        private const double S2 = 8.33333333332248946124e-03;
        private const double S3 = -1.98412698298579493134e-04;
        private const double S4 = 2.75573137070700676789e-06;
        private const double S5 = -2.50507602534068634195e-08;
        private const double S6 = 1.58969099521155010221e-10;

        private const double C1 = 4.16666666666666019037e-02;
        private const double C2 = -1.38888888888741095749e-03;
        private const double C3 = 2.48015872894767294178e-05;
        private const double C4 = -2.75573143513906633035e-07;
        private const double C5 = 2.08757232129817482790e-09;
        private const double C6 = -1.13596475577881948265e-11;

        private static int HighWord(double x) => (int)(BitConverter.DoubleToInt64Bits(x) >> 32);

        /// <summary>__kernel_sin: sin on [-pi/4, pi/4]. <paramref name="iy"/> is 0 when y is zero.</summary>
        private static double KernelSin(double x, double y, int iy)
        {
            int ix = HighWord(x) & 0x7fffffff;
            if (ix < 0x3e400000)            // |x| < 2**-27
            {
                if ((int)x == 0) return x;  // generate inexact
            }
            double z = x * x;
            double v = z * x;
            double r = S2 + z * (S3 + z * (S4 + z * (S5 + z * S6)));
            if (iy == 0) return x + v * (S1 + z * r);
            return x - ((z * (0.5 * y - v * r) - y) - v * S1);
        }

        /// <summary>__kernel_cos: cos on [-pi/4, pi/4].</summary>
        private static double KernelCos(double x, double y)
        {
            int ix = HighWord(x) & 0x7fffffff;
            if (ix < 0x3e400000)            // |x| < 2**-27
            {
                if ((int)x == 0) return 1.0;
            }
            double z = x * x;
            double r = z * (C1 + z * (C2 + z * (C3 + z * (C4 + z * (C5 + z * C6)))));
            if (ix < 0x3FD33333)            // |x| < 0.3
            {
                return 1.0 - (0.5 * z - (z * r - x * y));
            }
            double qx;
            if (ix > 0x3fe90000)            // x > 0.78125
            {
                qx = 0.28125;
            }
            else
            {
                qx = BitConverter.Int64BitsToDouble((long)(ix - 0x00200000) << 32);  // x/4
            }
            double hz = 0.5 * z - qx;
            double a = 1.0 - qx;
            return a - (hz - (z * r - x * y));
        }

        private const double InvPio2 = 6.36619772367581382433e-01;
        private const double Pio2_1 = 1.57079632673412561417e+00;
        private const double Pio2_1t = 6.07710050650619224932e-11;
        private const double Pio2_2 = 6.07710050630396597660e-11;
        private const double Pio2_2t = 2.02226624879595063154e-21;
        private const double Pio2_3 = 2.02226624871116645580e-21;
        private const double Pio2_3t = 8.47842766036889956997e-32;
        private const double TwoPow24 = 1.67772160000000000000e+07;

        /// <summary>
        /// __ieee754_rem_pio2: x = n*pi/2 + (y0 + y1), |y0 + y1| &lt;= pi/4.
        /// Returns n; the medium path only (see the note above).
        /// </summary>
        private static int RemPio2(double x, out double y0, out double y1)
        {
            int hx = HighWord(x);
            int ix = hx & 0x7fffffff;
            if (ix <= 0x3fe921fb)           // |x| ~<= pi/4
            {
                y0 = x; y1 = 0; return 0;
            }
            if (ix < 0x4002d97c)            // |x| < 3pi/4, special-cased for speed
            {
                double z;
                if (hx > 0)
                {
                    z = x - Pio2_1;
                    if (ix != 0x3ff921fb)
                    {
                        y0 = z - Pio2_1t;
                        y1 = (z - y0) - Pio2_1t;
                    }
                    else
                    {
                        z -= Pio2_2;
                        y0 = z - Pio2_2t;
                        y1 = (z - y0) - Pio2_2t;
                    }
                    return 1;
                }
                z = x + Pio2_1;
                if (ix != 0x3ff921fb)
                {
                    y0 = z + Pio2_1t;
                    y1 = (z - y0) + Pio2_1t;
                }
                else
                {
                    z += Pio2_2;
                    y0 = z + Pio2_2t;
                    y1 = (z - y0) + Pio2_2t;
                }
                return -1;
            }
            if (ix <= 0x413921fb)           // |x| ~<= 2^19 * (pi/2), medium size
            {
                double t = Math.Abs(x);
                int n = (int)(t * InvPio2 + 0.5);
                double fn = n;
                double r = t - fn * Pio2_1;
                double w = fn * Pio2_1t;    // first round good to 85 bits
                int j = ix >> 20;
                y0 = r - w;
                int i = j - ((HighWord(y0) >> 20) & 0x7ff);
                if (i > 16)                 // 2nd iteration needed, good to 118
                {
                    t = r;
                    w = fn * Pio2_2;
                    r = t - w;
                    w = fn * Pio2_2t - ((t - r) - w);
                    y0 = r - w;
                    i = j - ((HighWord(y0) >> 20) & 0x7ff);
                    if (i > 49)             // 3rd iteration, 151 bits
                    {
                        t = r;
                        w = fn * Pio2_3;
                        r = t - w;
                        w = fn * Pio2_3t - ((t - r) - w);
                        y0 = r - w;
                    }
                }
                y1 = (r - y0) - w;
                if (hx < 0) { y0 = -y0; y1 = -y1; return -n; }
                return n;
            }
            throw new ArgumentOutOfRangeException(nameof(x),
                "JsMath trig covers |x| < 2^19 * pi/2; the simulation should never pass more");
        }

        /// <summary><c>Math.sin</c>, bit-identical to V8 and to itself on every platform.</summary>
        public static double Sin(double x)
        {
            int ix = HighWord(x) & 0x7fffffff;
            if (ix <= 0x3fe921fb) return KernelSin(x, 0.0, 0);
            if (ix >= 0x7ff00000) return double.NaN;
            int n = RemPio2(x, out double y0, out double y1);
            switch (n & 3)
            {
                case 0: return KernelSin(y0, y1, 1);
                case 1: return KernelCos(y0, y1);
                case 2: return -KernelSin(y0, y1, 1);
                default: return -KernelCos(y0, y1);
            }
        }

        /// <summary><c>Math.cos</c>, bit-identical to V8 and to itself on every platform.</summary>
        public static double Cos(double x)
        {
            int ix = HighWord(x) & 0x7fffffff;
            if (ix <= 0x3fe921fb) return KernelCos(x, 0.0);
            if (ix >= 0x7ff00000) return double.NaN;
            int n = RemPio2(x, out double y0, out double y1);
            switch (n & 3)
            {
                case 0: return KernelCos(y0, y1);
                case 1: return -KernelSin(y0, y1, 1);
                case 2: return -KernelCos(y0, y1);
                default: return KernelSin(y0, y1, 1);
            }
        }
    }
}
