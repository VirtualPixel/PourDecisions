using System.Text;

namespace PourDecisions.Services
{
    // Slurs text as the team's Proof rises. Pure, deterministic, and TAG-AWARE: it never touches
    // anything inside <...>, [...] or {...}, so rich-text tags, [haul]/[goal] substitution tokens,
    // and {emoji} tokens pass through untouched. Determinism (a tiny xorshift seeded from the input,
    // the run seed, and the Proof bucket) means every client slurs an identical string - the shared
    // truck terminal stays in sync with no networking. Never uses System.Random / DateTime.
    internal static class Drunkify
    {
        private struct Rng
        {
            private uint _s;
            public Rng(uint seed) { _s = seed == 0 ? 2166136261u : seed; }
            public uint Next() { _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5; return _s; }
            public float Float() => (Next() & 0xFFFFFF) / (float)0x1000000;
            public bool Chance(float p) => Float() < p;
        }

        private static uint Hash(string s, int runSeed, int proofBucket)
        {
            uint h = 2166136261u;
            foreach (char c in s) { h ^= c; h *= 16777619u; }
            h ^= (uint)runSeed; h *= 16777619u;
            h ^= (uint)proofBucket; h *= 16777619u;
            return h;
        }

        // Apply slurring at the given proof [0,1]. text is returned unchanged when proof <= 0.
        public static string Apply(string text, float proof, int runSeed)
        {
            if (string.IsNullOrEmpty(text) || proof <= 0f) return text;
            if (proof > 1f) proof = 1f;

            int proofBucket = (int)(proof * 10f);
            var rng = new Rng(Hash(text, runSeed, proofBucket));
            var sb = new StringBuilder(text.Length + 16);

            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];

                // Copy any tag / variable / emoji span verbatim.
                char close = c == '<' ? '>' : c == '[' ? ']' : c == '{' ? '}' : '\0';
                if (close != '\0')
                {
                    int end = text.IndexOf(close, i);
                    if (end < 0) end = text.Length - 1;
                    sb.Append(text, i, end - i + 1);
                    i = end + 1;
                    continue;
                }

                // Slur a run of letters as one word.
                if (char.IsLetter(c))
                {
                    int start = i;
                    while (i < text.Length && char.IsLetter(text[i])) i++;
                    SlurWord(text, start, i, proof, ref rng, sb);

                    // Occasional hiccup between words.
                    if (proof > 0.3f && rng.Chance(0.06f * proof)) sb.Append(" *hic*");
                    continue;
                }

                sb.Append(c);
                i++;
            }

            return sb.ToString();
        }

        private static void SlurWord(string src, int start, int end, float proof, ref Rng rng, StringBuilder sb)
        {
            int len = end - start;
            for (int k = start; k < end; k++)
            {
                char ch = src[k];
                char lower = char.ToLowerInvariant(ch);

                // Slur trailing 's' to 'sh' at higher proof.
                if (proof > 0.45f && (lower == 's') && rng.Chance(0.5f * proof))
                {
                    sb.Append(ch);
                    sb.Append(char.IsUpper(ch) ? 'H' : 'h');
                    continue;
                }

                sb.Append(ch);

                // Double a consonant.
                if (proof > 0.15f && k > start && lower != 'a' && lower != 'e' && lower != 'i'
                    && lower != 'o' && lower != 'u' && rng.Chance(0.10f * proof))
                {
                    sb.Append(ch);
                }

                // Stretch a vowel.
                if ((lower == 'o' || lower == 'a' || lower == 'e') && rng.Chance(0.18f * proof))
                {
                    int extra = 1 + (proof > 0.6f ? 1 : 0);
                    for (int x = 0; x < extra; x++) sb.Append(ch);
                }
            }

            // High proof: words trail off.
            if (len > 3 && proof > 0.7f && rng.Chance(0.08f * proof)) sb.Append("...");
        }
    }
}
