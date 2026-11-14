using System.Text.Json;
using System.Text.Json.Nodes;
using SecRandom.Shared.DataRoot;

namespace SecRandom.Core.Services.DataRoot;

/// <summary>
///     Rewrites the data files that may carry an absolute path from the old data root. Only two shapes exist today:
///     attached draw-image settings under <c>list/</c> (<c>ImagePath</c>) and the proof attestation queue
///     (<c>path</c>). Values that live outside the old root — a user-picked external image, a URL — are left
///     byte-identical, because the migration must never claim to know what they meant.
/// </summary>
public static class DataRootPathRewriter
{
    private static readonly JsonSerializerOptions CompactWriteOptions = new()
    {
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions IndentedWriteOptions = new()
    {
        WriteIndented = true
    };

    private static readonly JsonNodeOptions NodeOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };

    /// <summary>Rewrites every rewritable file inside the staging copy and returns how many values changed.</summary>
    public static int RewriteStaging(string stagingRoot, string oldRoot, string newRoot)
    {
        var changed = 0;

        var listDirectory = Path.Combine(stagingRoot, "list");
        if (Directory.Exists(listDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(listDirectory, "*.json", SearchOption.AllDirectories))
                changed += RewriteFile(file, oldRoot, newRoot, imageOnly: true);
        }

        var attestationQueue = Path.Combine(stagingRoot, "proofs", "attestation-queue.json");
        if (File.Exists(attestationQueue))
            changed += RewriteFile(attestationQueue, oldRoot, newRoot, imageOnly: false);

        return changed;
    }

    private static int RewriteFile(string file, string oldRoot, string newRoot, bool imageOnly)
    {
        string original;
        try
        {
            original = File.ReadAllText(file);
        }
        catch (IOException)
        {
            return 0;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(original, NodeOptions);
        }
        catch (JsonException)
        {
            return 0;
        }

        if (root is null)
            return 0;

        var changed = RewriteNode(root, oldRoot, newRoot, imageOnly);
        if (changed == 0)
            return 0;

        // Keep the file's existing formatting: a compact settings file must stay compact, and an indented one
        // must not be flattened, otherwise the migration would produce a spurious whole-file diff.
        var writeOptions = original.Contains('\n') ? IndentedWriteOptions : CompactWriteOptions;
        File.WriteAllText(file, root.ToJsonString(writeOptions));
        return changed;
    }

    private static int RewriteNode(JsonNode? node, string oldRoot, string newRoot, bool imageOnly)
    {
        var changed = 0;

        switch (node)
        {
            case JsonObject jsonObject:
                foreach (var property in jsonObject.ToList())
                {
                    if (Matches(property.Key, imageOnly) &&
                        TryReadString(property.Value, out var text) &&
                        RewriteValue(text, oldRoot, newRoot) is var rewritten &&
                        !string.Equals(rewritten, text, StringComparison.Ordinal))
                    {
                        jsonObject[property.Key] = rewritten;
                        changed++;
                        continue;
                    }

                    changed += RewriteNode(property.Value, oldRoot, newRoot, imageOnly);
                }

                break;

            case JsonArray jsonArray:
                for (var index = 0; index < jsonArray.Count; index++)
                {
                    var item = jsonArray[index];
                    if (TryReadString(item, out var text) &&
                        RewriteValue(text, oldRoot, newRoot) is var rewritten &&
                        !string.Equals(rewritten, text, StringComparison.Ordinal))
                    {
                        jsonArray[index] = rewritten;
                        changed++;
                        continue;
                    }

                    changed += RewriteNode(item, oldRoot, newRoot, imageOnly);
                }

                break;
        }

        return changed;
    }

    private static bool Matches(string propertyName, bool imageOnly)
    {
        if (string.Equals(propertyName, "ImagePath", StringComparison.OrdinalIgnoreCase))
            return true;

        if (imageOnly)
            return false;

        return string.Equals(propertyName, "path", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(propertyName, "proofPath", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadString(JsonNode? node, out string text)
    {
        text = string.Empty;
        if (node is not JsonValue value || !value.TryGetValue<string>(out var parsed) || parsed is null)
            return false;

        text = parsed;
        return true;
    }

    private static string RewriteValue(string value, string oldRoot, string newRoot)
    {
        if (string.IsNullOrWhiteSpace(value) || DataPathReference.IsStoredRelative(value))
            return value;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(value.Trim());
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or
                                           PathTooLongException)
        {
            return value;
        }

        if (!DataRootPaths.IsSameOrNested(oldRoot, fullPath))
            return value;

        var relative = Path.GetRelativePath(oldRoot, fullPath);
        return relative.Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }
}