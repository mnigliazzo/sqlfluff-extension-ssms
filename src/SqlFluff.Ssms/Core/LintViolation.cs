namespace SqlFluff.Ssms.Core
{
    internal sealed class LintViolation
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        // 1-based; EndLine/EndColumn are 0 when sqlfluff did not report an end position.
        public int StartLine { get; set; }
        public int StartColumn { get; set; }
        public int EndLine { get; set; }
        public int EndColumn { get; set; }

        public bool IsParseError => Code == "PRS" || Code == "LXR" || Code == "TMP";

        // False for a rule sqlfluff reports but has no autofix for at all (e.g. AM04 "ambiguous
        // column count") - `sqlfluff fix --rules <code>` on one of these is a guaranteed no-op
        // ("Unfixable violations detected", exit code 1, output unchanged). The Light Bulb's
        // per-violation "Fix this issue" action must not be offered for these, or clicking it would
        // silently do nothing.
        public bool IsFixable { get; set; }

        public string Message =>
            string.IsNullOrEmpty(Name) ? Code + ": " + Description : Code + ": " + Description + " (" + Name + ")";
    }
}
