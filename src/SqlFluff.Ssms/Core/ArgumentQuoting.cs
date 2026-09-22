using System.Text;

namespace SqlFluff.Ssms.Core
{
    internal static class ArgumentQuoting
    {
        // Windows command-line argument quoting (CommandLineToArgvW rules).
        public static string Quote(string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0)
            {
                return arg;
            }

            var sb = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in arg)
            {
                if (c == '\\')
                {
                    backslashes++;
                }
                else if (c == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1).Append('"');
                    backslashes = 0;
                }
                else
                {
                    sb.Append('\\', backslashes).Append(c);
                    backslashes = 0;
                }
            }

            sb.Append('\\', backslashes * 2).Append('"');
            return sb.ToString();
        }
    }
}
