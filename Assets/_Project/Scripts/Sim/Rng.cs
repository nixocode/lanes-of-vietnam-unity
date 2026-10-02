using System;

namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// mulberry32, ported exactly from the three.js build.
    ///
    /// Exactly, and that word is doing work: the whole determinism guarantee
    /// rests on this producing the same sequence as the TypeScript original,
    /// so the balance numbers measured over 48 seeds there still mean
    /// something here. Every operation below is 32-bit unsigned on purpose.
    /// C# ints are signed and JavaScript's <c>&gt;&gt;&gt;</c> and
    /// <c>Math.imul</c> have no direct equivalent, so the casts are not noise
    /// and must not be "cleaned up".
    ///
    /// Not <see cref="UnityEngine.Random"/>, and never that: it is global
    /// mutable state shared with every other system in the engine, which means
    /// a particle effect drawn on one frame could shift the simulation. The
    /// Sim assembly has <c>noEngineReferences</c> set precisely so that this
    /// cannot be done by accident.
    /// </summary>
    public sealed class Rng
    {
        private uint _s;

        public Rng(int seed)
        {
            // splitmix-style scramble of the seed itself, so that seeds 1000
            // and 1001 start from genuinely different states rather than
            // adjacent ones. Without this, neighbouring seeds produce
            // correlated matches and a "48 seeds" balance run is really about
            // six independent ones.
            unchecked
            {
                uint z = (uint)seed;
                z += 0x9e3779b9u;
                z = Imul(z ^ (z >> 16), 0x21f0aaadu);
                z = Imul(z ^ (z >> 15), 0x735a2d97u);
                _s = z ^ (z >> 15);
            }
        }

        // No raw-state constructor. `Fork` below goes through the public,
        // scrambling one because the TypeScript original does — it calls
        // `new Rng(s ^ h)`, and that constructor splitmixes its argument. A
        // private ctor that assigned the state directly would look equivalent,
        // produce a perfectly good random sequence, and silently diverge from
        // every balance number measured in the previous build.

        /// <summary>JavaScript's <c>Math.imul</c>: 32-bit integer multiply.</summary>
        private static uint Imul(uint a, uint b)
        {
            unchecked { return a * b; }
        }

        /// <summary>Uniform in [0, 1).</summary>
        public double Next()
        {
            unchecked
            {
                _s += 0x6d2b79f5u;
                uint t = _s;
                t = Imul(t ^ (t >> 15), t | 1u);
                t ^= t + Imul(t ^ (t >> 7), t | 61u);
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }

        public double Range(double lo, double hi) => lo + (hi - lo) * Next();

        public int Int(int loInclusive, int hiExclusive)
            => loInclusive + (int)(Next() * (hiExclusive - loInclusive));

        public T Pick<T>(System.Collections.Generic.IReadOnlyList<T> xs)
        {
            if (xs.Count == 0) throw new InvalidOperationException("pick from empty list");
            return xs[(int)(Next() * xs.Count)];
        }

        /// <summary>
        /// A named, independent stream from this one.
        ///
        /// This is the mechanism that keeps the renderer from perturbing the
        /// simulation. The view takes <c>Fork("effects")</c> and scatters its
        /// impact dust from that; if it drew from the shared stream instead,
        /// the number of cosmetic draws in a browser frame would shift the
        /// match and a headless run would stop reproducing it. That exact bug
        /// was written, and caught, in the three.js build.
        ///
        /// A fork is reproducible from the seed and the name alone — it does
        /// not depend on how much of the parent stream has been consumed,
        /// which is what makes it safe to add a fork later without moving
        /// every match that came before.
        /// </summary>
        public Rng Fork(string name)
        {
            unchecked
            {
                uint h = 0x811c9dc5u;
                for (int i = 0; i < name.Length; i++)
                {
                    h = Imul(h ^ name[i], 0x01000193u);
                }
                return new Rng(unchecked((int)(_s ^ h)));
            }
        }

        /// <summary>The raw state, for tests and trace hashing.</summary>
        public uint State => _s;

        /// <summary>
        /// Pearson correlation between the first draw of seed n and of seed
        /// n + 1, over a block of seeds.
        ///
        /// Brief §9 finding 9, the one that cost the most: the 2D game's LCG
        /// made each match reproducible and a block of seeds worthless —
        /// 0.998 here — so one mission read 8/12 on one block and 0/12 on the
        /// next with identical code. Near zero means consecutive seeds are a
        /// usable sample. Run in the tests, not trusted.
        /// </summary>
        public static double AdjacentSeedCorrelation(int from, int count, Func<Rng, double> draw = null)
        {
            draw ??= r => r.Next();
            var a = new double[count];
            var b = new double[count];
            for (int i = 0; i < count; i++)
            {
                a[i] = draw(new Rng(from + i));
                b[i] = draw(new Rng(from + i + 1));
            }
            double ma = 0, mb = 0;
            for (int i = 0; i < count; i++) { ma += a[i]; mb += b[i]; }
            ma /= count; mb /= count;
            double num = 0, da = 0, db = 0;
            for (int i = 0; i < count; i++)
            {
                double x = a[i] - ma, y = b[i] - mb;
                num += x * y; da += x * x; db += y * y;
            }
            double den = Math.Sqrt(da * db);
            return den == 0 ? 0 : num / den;
        }
    }
}
