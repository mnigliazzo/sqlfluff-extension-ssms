using System;
using System.Collections.Generic;

namespace SqlFluff.Ssms.Core
{
    // A contiguous range of lines that differs between the two inputs to LineDiff.ComputeHunks.
    // Used to accept sqlfluff's rewrite for just the hunk touching one specific violation's line
    // while discarding its rewrite of every other occurrence of that rule elsewhere in the file —
    // sqlfluff itself has no way to fix a single violation instance by position, only by rule
    // across a whole span of text (see LintService.FixRuleAsync).
    internal readonly struct LineHunk
    {
        public LineHunk(int originalStart, int originalEnd, IReadOnlyList<string> newLines)
        {
            OriginalStart = originalStart;
            OriginalEnd = originalEnd;
            NewLines = newLines;
        }

        // 0-based, half-open [OriginalStart, OriginalEnd) into the original line array. Empty
        // (OriginalStart == OriginalEnd) for a pure insertion.
        public int OriginalStart { get; }
        public int OriginalEnd { get; }
        public IReadOnlyList<string> NewLines { get; }
    }

    internal static class LineDiff
    {
        // Classic LCS-based line diff: walks the two inputs, preferring to consume the side with
        // the longer remaining common subsequence at each divergence, and groups the resulting
        // insertions/deletions into hunks separated by runs of matching lines.
        public static IReadOnlyList<LineHunk> ComputeHunks(IReadOnlyList<string> original, IReadOnlyList<string> updated)
        {
            int n = original.Count;
            int m = updated.Count;

            var lcs = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
            {
                for (int j = m - 1; j >= 0; j--)
                {
                    lcs[i, j] = original[i] == updated[j]
                        ? lcs[i + 1, j + 1] + 1
                        : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
                }
            }

            var hunks = new List<LineHunk>();
            int ai = 0, bi = 0;
            int hunkStart = -1;
            var added = new List<string>();

            void Flush(int end)
            {
                if (hunkStart >= 0)
                {
                    hunks.Add(new LineHunk(hunkStart, end, added.ToArray()));
                }

                hunkStart = -1;
                added = new List<string>();
            }

            while (ai < n && bi < m)
            {
                if (original[ai] == updated[bi])
                {
                    Flush(ai);
                    ai++;
                    bi++;
                }
                else
                {
                    if (hunkStart < 0)
                    {
                        hunkStart = ai;
                    }

                    if (lcs[ai + 1, bi] >= lcs[ai, bi + 1])
                    {
                        ai++;
                    }
                    else
                    {
                        added.Add(updated[bi]);
                        bi++;
                    }
                }
            }

            while (bi < m)
            {
                if (hunkStart < 0)
                {
                    hunkStart = ai;
                }

                added.Add(updated[bi]);
                bi++;
            }

            if (ai < n && hunkStart < 0)
            {
                hunkStart = ai;
            }

            Flush(n);

            return hunks;
        }

        // True when a 1-based original line number falls inside the hunk's original range, or —
        // for a pure insertion (empty range) — sits exactly at the insertion point.
        public static bool HunkTouchesLine(LineHunk hunk, int originalLine1Based)
        {
            int line0 = originalLine1Based - 1;
            if (hunk.OriginalStart < hunk.OriginalEnd)
            {
                return line0 >= hunk.OriginalStart && line0 < hunk.OriginalEnd;
            }

            return line0 == hunk.OriginalStart;
        }

        // Reconstructs the line sequence keeping only the hunks `shouldApply` accepts; every other
        // hunk (and every unchanged, matched line) is taken from `original`.
        public static IReadOnlyList<string> ApplySelectedHunks(
            IReadOnlyList<string> original, IReadOnlyList<LineHunk> hunks, Func<LineHunk, bool> shouldApply)
        {
            var result = new List<string>(original.Count);
            int cursor = 0;

            foreach (LineHunk hunk in hunks)
            {
                for (int i = cursor; i < hunk.OriginalStart; i++)
                {
                    result.Add(original[i]);
                }

                if (shouldApply(hunk))
                {
                    result.AddRange(hunk.NewLines);
                }
                else
                {
                    for (int i = hunk.OriginalStart; i < hunk.OriginalEnd; i++)
                    {
                        result.Add(original[i]);
                    }
                }

                cursor = hunk.OriginalEnd;
            }

            for (int i = cursor; i < original.Count; i++)
            {
                result.Add(original[i]);
            }

            return result;
        }
    }
}
