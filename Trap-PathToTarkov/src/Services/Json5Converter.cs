using System;
using System.Text;
using System.Text.RegularExpressions;

namespace PathToTarkov.Services;

/// <summary>
/// Converts json5 to standard JSON so System.Text.Json can parse it.
/// Handles: unquoted keys, single-quoted strings, trailing commas,
/// // and /* */ comments, and infinity/NaN literals.
/// </summary>
public static class Json5Converter
{
    public static string ToJson(string json5)
    {
        var s = json5;

        // 1. Strip block comments /* ... */
        s = Regex.Replace(s, @"/\*.*?\*/", " ", RegexOptions.Singleline);

        // 2. Strip line comments // ...  (but not inside strings)
        s = StripLineComments(s);

        // 3. Convert single-quoted strings to double-quoted
        s = ConvertSingleQuotes(s);

        // 4. Quote unquoted object keys  (word chars before a colon)
        s = Regex.Replace(s, @"([{,]\s*)([A-Za-z_$][A-Za-z0-9_$]*)(\s*:)",
                          m => m.Groups[1].Value + "\"" + m.Groups[2].Value + "\"" + m.Groups[3].Value);

        // 5. Remove trailing commas before } or ]
        s = Regex.Replace(s, @",(\s*[}\]])", "$1");

        // 6. Replace unquoted Infinity / NaN / undefined
        s = Regex.Replace(s, @"\bInfinity\b",  "1e308");
        s = Regex.Replace(s, @"\bNaN\b",       "null");
        s = Regex.Replace(s, @"\bundefined\b", "null");

        return s;
    }

    // ---- Single-quote conversion ----
    // Walks the string char-by-char tracking whether we're inside a string.

    private static string ConvertSingleQuotes(string s)
    {
        var sb  = new StringBuilder(s.Length);
        bool inDouble = false;
        bool inSingle = false;
        int  i = 0;

        while (i < s.Length)
        {
            char c = s[i];

            if (!inSingle && c == '"')  { inDouble = !inDouble; sb.Append(c); i++; continue; }
            if (!inDouble && c == '\'') { inSingle = !inSingle; sb.Append('"'); i++; continue; }

            // Handle escapes inside strings
            if ((inDouble || inSingle) && c == '\\' && i + 1 < s.Length)
            {
                char next = s[i + 1];
                // In single-quoted strings, \' → just ' (remove the backslash)
                // and \" would be unnecessary but keep it
                if (inSingle && next == '\'') { sb.Append('\''); i += 2; continue; }
                // Escape double-quote inside a converted single-quoted string
                if (inSingle && next == '"')  { sb.Append("\\\""); i += 2; continue; }
                sb.Append(c);
                sb.Append(next);
                i += 2;
                continue;
            }

            // Inside a converted single-quoted string, escape bare double-quotes
            if (inSingle && c == '"') { sb.Append("\\\""); i++; continue; }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    // ---- Line-comment stripper that respects strings ----

    private static string StripLineComments(string s)
    {
        var sb      = new StringBuilder(s.Length);
        bool inDouble = false;
        bool inSingle = false;
        int  i = 0;

        while (i < s.Length)
        {
            char c = s[i];

            if (!inSingle && c == '"')  { inDouble = !inDouble; sb.Append(c); i++; continue; }
            if (!inDouble && c == '\'') { inSingle = !inSingle; sb.Append(c); i++; continue; }

            if (!inDouble && !inSingle && c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                // skip to end of line
                while (i < s.Length && s[i] != '\n') i++;
                continue;
            }

            if ((inDouble || inSingle) && c == '\\' && i + 1 < s.Length)
            {
                sb.Append(c); sb.Append(s[i + 1]); i += 2; continue;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }
}
