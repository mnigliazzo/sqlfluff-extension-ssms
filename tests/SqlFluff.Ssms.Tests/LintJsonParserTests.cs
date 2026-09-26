using System.Linq;
using SqlFluff.Ssms.Core;
using Xunit;

namespace SqlFluff.Ssms.Tests
{
    public class LintJsonParserTests
    {
        [Fact]
        public void Parse_ReturnsEmptyList_WhenNoViolations()
        {
            const string json = @"[{""filepath"": ""stdin"", ""violations"": []}]";

            var result = LintJsonParser.Parse(json);

            Assert.Empty(result);
        }

        [Fact]
        public void Parse_ReturnsEmptyList_WhenNoFilesAtAll()
        {
            var result = LintJsonParser.Parse("[]");

            Assert.Empty(result);
        }

        [Fact]
        public void Parse_MapsAllFieldsFromARealViolation()
        {
            const string json = @"[{
                ""filepath"": ""stdin"",
                ""violations"": [{
                    ""start_line_no"": 3,
                    ""start_line_pos"": 7,
                    ""code"": ""LT01"",
                    ""name"": ""layout.spacing"",
                    ""description"": ""Expected only single space before naked identifier.""
                }]
            }]";

            var result = LintJsonParser.Parse(json);

            LintViolation violation = Assert.Single(result);
            Assert.Equal("LT01", violation.Code);
            Assert.Equal("layout.spacing", violation.Name);
            Assert.Equal("Expected only single space before naked identifier.", violation.Description);
            Assert.Equal(3, violation.StartLine);
            Assert.Equal(7, violation.StartColumn);
        }

        [Fact]
        public void Parse_DefaultsMissingLineAndColumnToOne()
        {
            const string json = @"[{""filepath"": ""stdin"", ""violations"": [{""code"": ""PRS"", ""description"": ""parse error""}]}]";

            var result = LintJsonParser.Parse(json);

            LintViolation violation = Assert.Single(result);
            Assert.Equal(1, violation.StartLine);
            Assert.Equal(1, violation.StartColumn);
            Assert.Equal(0, violation.EndLine);
            Assert.Equal(0, violation.EndColumn);
        }

        [Fact]
        public void Parse_MarksViolationFixable_WhenFixesArrayIsNonEmpty()
        {
            const string json = @"[{""filepath"": ""stdin"", ""violations"": [{
                ""code"": ""LT01"",
                ""description"": ""spacing"",
                ""fixes"": [{""type"": ""create_after"", ""edit"": "" ""}]
            }]}]";

            LintViolation violation = Assert.Single(LintJsonParser.Parse(json));

            Assert.True(violation.IsFixable);
        }

        [Fact]
        public void Parse_MarksViolationNotFixable_WhenFixesArrayIsEmpty()
        {
            // Real shape of an unfixable rule, e.g. AM04 "ambiguous column count" - sqlfluff reports
            // the violation but an empty "fixes" array, and `sqlfluff fix --rules AM04` is a no-op.
            const string json = @"[{""filepath"": ""stdin"", ""violations"": [{
                ""code"": ""AM04"",
                ""description"": ""ambiguous column count"",
                ""fixes"": []
            }]}]";

            LintViolation violation = Assert.Single(LintJsonParser.Parse(json));

            Assert.False(violation.IsFixable);
        }

        [Fact]
        public void Parse_MarksViolationFixable_WhenFixesFieldIsMissing()
        {
            // Older sqlfluff versions may not emit "fixes" at all - treat that as "assume fixable"
            // (the pre-IsFixable behavior) rather than silently disabling every "Fix this issue"
            // Light Bulb action against an older tool.
            const string json = @"[{""filepath"": ""stdin"", ""violations"": [{""code"": ""LT01"", ""description"": ""spacing""}]}]";

            LintViolation violation = Assert.Single(LintJsonParser.Parse(json));

            Assert.True(violation.IsFixable);
        }

        [Fact]
        public void Parse_TreatsParseErrorCodesAsParseErrors()
        {
            const string json = @"[{""filepath"": ""stdin"", ""violations"": [{""code"": ""PRS"", ""description"": ""parse error""}]}]";

            LintViolation violation = Assert.Single(LintJsonParser.Parse(json));

            Assert.True(violation.IsParseError);
        }

        [Fact]
        public void Parse_HandlesMultipleFilesAndMultipleViolations()
        {
            const string json = @"[
                {""filepath"": ""a.sql"", ""violations"": [{""code"": ""LT01"", ""description"": ""a""}, {""code"": ""LT02"", ""description"": ""b""}]},
                {""filepath"": ""b.sql"", ""violations"": [{""code"": ""AM04"", ""description"": ""c""}]}
            ]";

            var result = LintJsonParser.Parse(json);

            Assert.Equal(3, result.Count);
            Assert.Equal(new[] { "LT01", "LT02", "AM04" }, result.Select(v => v.Code));
        }

        [Fact]
        public void Parse_IgnoresLeadingNonJsonNoise()
        {
            // sqlfluff's --format json output can be preceded by warnings on stdout in some
            // configurations; the parser looks for the first '[' rather than requiring position 0.
            const string json = @"Some warning line before the payload
[{""filepath"": ""stdin"", ""violations"": []}]";

            var result = LintJsonParser.Parse(json);

            Assert.Empty(result);
        }

        [Fact]
        public void Parse_ThrowsLintJsonParseException_WhenNoJsonArrayPresent()
        {
            Assert.Throws<LintJsonParseException>(() => LintJsonParser.Parse("sqlfluff: command not found"));
        }

        [Fact]
        public void Parse_ThrowsLintJsonParseException_WhenJsonIsMalformed()
        {
            Assert.Throws<LintJsonParseException>(() => LintJsonParser.Parse("[{\"filepath\": \"stdin\", \"violations\": [}]"));
        }
    }
}
