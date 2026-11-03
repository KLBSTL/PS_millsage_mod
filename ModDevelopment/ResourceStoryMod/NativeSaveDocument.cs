using System.Text;
using Newtonsoft.Json.Linq;

namespace ResourceStoryMod
{
    // ES3 writes integer dictionary keys without quotes, including negative window IDs.
    // ES3 also escapes typographic quotes in dialogue history. Adapt only
    // these native deviations in memory; never rewrite saved bytes to validate.
    internal static class NativeSaveDocument
    {
        internal static JObject Parse(string source)
        {
            var result = new StringBuilder(source.Length);
            char previous = '\0';
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (c == '"')
                {
                    result.Append(c);
                    while (++i < source.Length)
                    {
                        c = source[i];
                        if (c == '\\' && i + 1 < source.Length) {
                            char escaped=source[++i];
                            if(escaped is '“' or '”' or '‘' or '’')result.Append(escaped);
                            else result.Append('\\').Append(escaped);
                        }else {
                            result.Append(c);
                            if(c=='"')break;
                        }
                    }
                    previous = '"';
                    continue;
                }
                if ((previous == '{' || previous == ',') && (c == '-' || Digit(c)))
                {
                    int end = i + (c == '-' ? 1 : 0), start = end;
                    while (end < source.Length && Digit(source[end])) end++;
                    int colon = end;
                    while (colon < source.Length && char.IsWhiteSpace(source[colon])) colon++;
                    if (end > start && colon < source.Length && source[colon] == ':')
                    {
                        result.Append('"').Append(source, i, end - i).Append('"');
                        i = end - 1;
                        previous = '"';
                        continue;
                    }
                }
                result.Append(c);
                if (!char.IsWhiteSpace(c)) previous = c;
            }
            return JObject.Parse(result.ToString());
        }

        private static bool Digit(char c) => c >= '0' && c <= '9';
    }
}
