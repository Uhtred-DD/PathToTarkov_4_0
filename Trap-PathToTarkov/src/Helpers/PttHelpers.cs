using System;
using System.Collections.Generic;
using System.Linq;
using PathToTarkov.Models;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace PathToTarkov.Helpers;

public static class PttHelpers
{
    public const string PTT_INFILTRATION = "ptt_infiltration";

    // ---- AccessVia helpers ----

    public static bool IsWildcard(object? accessVia)
    {
        if (accessVia is string s) return s == "*";
        if (accessVia is List<object> list) return list.Count > 0 && list[0].ToString() == "*";
        if (accessVia is System.Text.Json.JsonElement el)
        {
            if (el.ValueKind == System.Text.Json.JsonValueKind.String)
                return el.GetString() == "*";
            if (el.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var first = el.EnumerateArray().FirstOrDefault();
                return first.ValueKind == System.Text.Json.JsonValueKind.String
                       && first.GetString() == "*";
            }
        }
        return false;
    }

    public static bool CheckAccessVia(object? accessVia, string value)
    {
        if (IsWildcard(accessVia)) return true;

        var list = NormalizeAccessVia(accessVia);
        return list.Contains(value);
    }

    public static List<string> NormalizeAccessVia(object? accessVia)
    {
        if (accessVia == null) return new();
        if (accessVia is string s) return new() { s };
        if (accessVia is List<string> ls) return ls;
        if (accessVia is System.Text.Json.JsonElement el)
        {
            if (el.ValueKind == System.Text.Json.JsonValueKind.String)
                return new() { el.GetString()! };
            if (el.ValueKind == System.Text.Json.JsonValueKind.Array)
                return el.EnumerateArray()
                         .Where(e => e.ValueKind == System.Text.Json.JsonValueKind.String)
                         .Select(e => e.GetString()!)
                         .ToList();
        }
        return new();
    }
}
