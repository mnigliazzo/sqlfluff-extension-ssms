using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SqlFluff.Ssms.Core
{
    internal sealed class LintJsonParseException : Exception
    {
        public LintJsonParseException(string message) : base(message) { }
    }

    // Parses `sqlfluff lint --format json` stdout. Pulled out of SqlFluffRunner so it can be unit
    // tested without touching Process/VS SDK types.
    internal static class LintJsonParser
    {
        public static IReadOnlyList<LintViolation> Parse(string stdout)
        {
            int start = stdout.IndexOf('[');
            if (start < 0)
            {
                throw new LintJsonParseException("Unexpected SQLFluff output (no JSON found).");
            }

            List<LintFile> files;
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(List<LintFile>));
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(stdout.Substring(start))))
                {
                    files = (List<LintFile>)serializer.ReadObject(ms);
                }
            }
            catch (Exception ex) when (ex is SerializationException || ex is InvalidCastException || ex is FormatException)
            {
                throw new LintJsonParseException("Could not parse SQLFluff JSON output: " + ex.Message);
            }

            var violations = new List<LintViolation>();
            foreach (LintFile file in files ?? new List<LintFile>())
            {
                foreach (LintItem item in file.Violations ?? new List<LintItem>())
                {
                    violations.Add(new LintViolation
                    {
                        Code = item.Code ?? string.Empty,
                        Name = item.Name,
                        Description = item.Description ?? string.Empty,
                        StartLine = Math.Max(1, item.StartLine ?? 1),
                        StartColumn = Math.Max(1, item.StartColumn ?? 1),
                        EndLine = item.EndLine ?? 0,
                        EndColumn = item.EndColumn ?? 0,
                    });
                }
            }

            return violations;
        }

        [DataContract]
        private sealed class LintFile
        {
            [DataMember(Name = "violations")]
            public List<LintItem> Violations { get; set; }
        }

        [DataContract]
        private sealed class LintItem
        {
            [DataMember(Name = "code")] public string Code { get; set; }
            [DataMember(Name = "name")] public string Name { get; set; }
            [DataMember(Name = "description")] public string Description { get; set; }
            [DataMember(Name = "start_line_no")] public int? StartLine { get; set; }
            [DataMember(Name = "start_line_pos")] public int? StartColumn { get; set; }
            [DataMember(Name = "end_line_no")] public int? EndLine { get; set; }
            [DataMember(Name = "end_line_pos")] public int? EndColumn { get; set; }
        }
    }
}
