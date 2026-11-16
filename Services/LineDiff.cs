namespace ClineWin
{
    public enum DiffKind { Context, Added, Removed, Gap }

    /// <summary>Una riga del diff, con i numeri di riga nel file prima e dopo (null se non c'è).</summary>
    public sealed record DiffLine(DiffKind Kind, string Text, int? OldNumber, int? NewNumber);

    /// <summary>Risultato: righe da mostrare (con contesto e salti) e conteggi.</summary>
    public sealed record DiffResult(IReadOnlyList<DiffLine> Lines, int Added, int Removed);

    /// <summary>
    /// Diff per righe tra due testi (sottosequenza comune più lunga), raggruppato in blocchi
    /// con alcune righe di contesto, come un "unified diff".
    /// </summary>
    public static class LineDiff
    {
        // Oltre questa dimensione (righe vecchie × nuove) il confronto completo costerebbe troppo:
        // si tolgono prima le parti uguali all'inizio e alla fine, e se resta troppo si mostra tutto come sostituito.
        private const long MaxCells = 4_000_000;

        public static DiffResult Compute(string oldText, string newText, int context = 3)
        {
            var a = SplitLines(oldText);
            var b = SplitLines(newText);

            // Parti uguali in testa e in coda: non serve confrontarle.
            int prefix = 0;
            while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix]) prefix++;
            int suffix = 0;
            while (suffix < a.Length - prefix && suffix < b.Length - prefix && a[^(suffix + 1)] == b[^(suffix + 1)]) suffix++;

            var ops = new List<(DiffKind Kind, int OldIndex, int NewIndex)>();
            for (int i = 0; i < prefix; i++) ops.Add((DiffKind.Context, i, i));
            ops.AddRange(Middle(a, b, prefix, a.Length - suffix, prefix, b.Length - suffix));
            for (int i = 0; i < suffix; i++) ops.Add((DiffKind.Context, a.Length - suffix + i, b.Length - suffix + i));

            int added = ops.Count(o => o.Kind == DiffKind.Added);
            int removed = ops.Count(o => o.Kind == DiffKind.Removed);
            return new DiffResult(WithContext(ops, a, b, context), added, removed);
        }

        private static string[] SplitLines(string text)
        {
            if (text.Length == 0) return [];
            var lines = text.Replace("\r\n", "\n").Split('\n');
            // Un "a capo" finale non crea una riga vuota in più.
            return lines.Length > 0 && lines[^1].Length == 0 ? lines[..^1] : lines;
        }

        /// <summary>Confronto della parte centrale con la tabella LCS.</summary>
        private static IEnumerable<(DiffKind, int, int)> Middle(string[] a, string[] b, int aStart, int aEnd, int bStart, int bEnd)
        {
            int n = aEnd - aStart, m = bEnd - bStart;
            var result = new List<(DiffKind, int, int)>();
            if ((long)n * m > MaxCells)
            {
                for (int i = 0; i < n; i++) result.Add((DiffKind.Removed, aStart + i, -1));
                for (int j = 0; j < m; j++) result.Add((DiffKind.Added, -1, bStart + j));
                return result;
            }

            // lcs[i, j] = lunghezza della sottosequenza comune di a[i..] e b[j..].
            var lcs = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
                for (int j = m - 1; j >= 0; j--)
                    lcs[i, j] = a[aStart + i] == b[bStart + j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

            int x = 0, y = 0;
            while (x < n && y < m)
            {
                if (a[aStart + x] == b[bStart + y]) { result.Add((DiffKind.Context, aStart + x, bStart + y)); x++; y++; }
                else if (lcs[x + 1, y] >= lcs[x, y + 1]) { result.Add((DiffKind.Removed, aStart + x, -1)); x++; }
                else { result.Add((DiffKind.Added, -1, bStart + y)); y++; }
            }
            for (; x < n; x++) result.Add((DiffKind.Removed, aStart + x, -1));
            for (; y < m; y++) result.Add((DiffKind.Added, -1, bStart + y));
            return result;
        }

        /// <summary>Tiene solo le righe cambiate e <paramref name="context"/> righe intorno; il resto diventa un "salto".</summary>
        private static List<DiffLine> WithContext(List<(DiffKind Kind, int OldIndex, int NewIndex)> ops, string[] a, string[] b, int context)
        {
            var keep = new bool[ops.Count];
            for (int i = 0; i < ops.Count; i++)
            {
                if (ops[i].Kind == DiffKind.Context) continue;
                for (int k = Math.Max(0, i - context); k <= Math.Min(ops.Count - 1, i + context); k++) keep[k] = true;
            }

            var lines = new List<DiffLine>();
            int skipped = 0;
            for (int i = 0; i < ops.Count; i++)
            {
                if (!keep[i]) { skipped++; continue; }
                if (skipped > 0)
                {
                    lines.Add(new DiffLine(DiffKind.Gap, $"{skipped} righe invariate", null, null));
                    skipped = 0;
                }
                var (kind, oldIndex, newIndex) = ops[i];
                string text = kind == DiffKind.Added ? b[newIndex] : a[oldIndex];
                lines.Add(new DiffLine(kind, text,
                    oldIndex >= 0 ? oldIndex + 1 : null,
                    newIndex >= 0 ? newIndex + 1 : null));
            }
            if (skipped > 0 && lines.Count > 0) lines.Add(new DiffLine(DiffKind.Gap, $"{skipped} righe invariate", null, null));
            return lines;
        }
    }
}
