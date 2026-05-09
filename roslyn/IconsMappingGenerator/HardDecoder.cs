using System;
using System.Collections.Generic;
using System.Linq;

namespace IconsMappingGenerator;

public static class HardDecoder
{
    private static readonly char[] TrimList = [' ', '"', ','];

    public static Dictionary<string, int> ParseJson(string jsonString)
    {
        Dictionary<string, int> registry = [];
        foreach (var line in jsonString.Split(["\r\n", "\n", "\r"], StringSplitOptions.None))
        {
            if (!line.Contains(':')) continue;
            var resolved = line.Split(':');
            registry.Add(resolved[0].Trim(TrimList), Convert.ToInt32(resolved[1].Trim(TrimList)));
        }

        return registry;
    }
}