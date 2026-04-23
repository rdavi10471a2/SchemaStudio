using SchemaStudioWebViewer.WEBSemanticModel.Model;
using System.Text.RegularExpressions;

namespace SchemaStudioWebViewer.WEBSemanticModel.Parsing
{
    public static class ViewMetadataBinder
    {
        // Singleline allows the dot (.) to match newlines within the comment block
        private const RegexOptions CommonOptions = RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline;

        private static readonly Regex NameRegex = new Regex(@"@BusinessName[:\s]*(?<val>.*?)(?=@|$)", CommonOptions);
        private static readonly Regex DescRegex = new Regex(@"@BusinessDescription[:\s]*(?<val>.*?)(?=@|$)", CommonOptions);
        // private static readonly Regex NoteRegex = new Regex(@"@DeveloperNotes[:\s]*(?<val>.*?)(?=@|$)", CommonOptions);

        public static void Apply(ParsedQuery query)
        {
            if (query == null) return;

            foreach (var item in query.SelectItems)
            {
                if (string.IsNullOrWhiteSpace(item.Comment)) continue;

                // 1. Clean the outer shell of the SQL comment
                // This removes --, /*, and importantly, the trailing */
                string cleanComment = item.Comment.Trim()
                    .TrimStart('-', '/', '*')
                    .TrimEnd('*', '/', ' ')
                    .Trim();

                item.BusinessName = ExtractTag(cleanComment, NameRegex);
                item.BusinessDescription = ExtractTag(cleanComment, DescRegex);
                // Developer notes are user-owned in the web workflow now, so leave parser-derived notes blank.
                // item.DeveloperNotes = ExtractTag(cleanComment, NoteRegex);
            }
        }

        private static string ExtractTag(string input, Regex regex)
        {
            var match = regex.Match(input);
            if (match.Success)
            {
                // The Triple-Trim Strategy:
                // 1. Outer Trim: Removes spaces/newlines captured by the lazy dot-match
                // 2. TrimStart(':'): Specifically targets the colon separator
                // 3. Final Trim: Ensures the resulting data has no leading/trailing junk
                return match.Groups["val"].Value
                    .Trim()
                    .TrimStart(':')
                    .Trim();
            }
            return null;
        }
    }
}
