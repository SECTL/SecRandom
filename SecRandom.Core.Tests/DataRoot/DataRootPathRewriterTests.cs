using Microsoft.Data.Sqlite;
using SecRandom.Core.Services.DataRoot;

namespace SecRandom.Core.Tests.DataRoot;

public sealed class DataRootPathRewriterTests
{
    [Fact]
    public void RewriteStaging_MakesRootedReferencesRelativeAndKeepsExternalOnes()
    {
        using var env = new DataRootTestEnvironment();
        var staging = env.CreateDirectory("staging");
        var oldRoot = env.PathOf("old-data");
        var newRoot = env.PathOf("new-data");
        var internalImage = env.PathOf("old-data", "images", "a.png");
        var externalImage = env.PathOf("pictures", "outside.png");

        env.WriteFile("staging/list/students.json",
            "{\"members\":[{\"ImagePath\":\"" + internalImage.Replace("\\", "\\\\") + "\"},"
            + "{\"ImagePath\":\"" + externalImage.Replace("\\", "\\\\") + "\"}]}");

        DataRootPathRewriter.RewriteStaging(staging, oldRoot, newRoot);

        var rewritten = env.ReadFile("staging/list/students.json");
        Assert.Contains("\"ImagePath\":\"images/a.png\"", rewritten, StringComparison.Ordinal);
        Assert.Contains(externalImage.Replace("\\", "\\\\"), rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void RewriteStaging_RewritesRosterSqlitePayloadsAndReturnsValueCount()
    {
        using var env = new DataRootTestEnvironment();
        var staging = env.CreateDirectory("staging");
        var databasePath = env.PathOf("staging", "list", "roll_call_list", "class.srl");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);" +
                                  "CREATE TABLE members (ordinal INTEGER PRIMARY KEY, payload TEXT NOT NULL);" +
                                  "CREATE TABLE attached_objects (control_id TEXT PRIMARY KEY, payload TEXT NOT NULL);" +
                                  "INSERT INTO meta VALUES ('format', 'SecRandomRosterDatabase/v1');" +
                                  "INSERT INTO meta VALUES ('schema_version', '1');" +
                                  "INSERT INTO members VALUES (0, $member);" +
                                  "INSERT INTO attached_objects VALUES ('control', $attached);";
            command.Parameters.AddWithValue("$member", "{\"ImagePath\":\"" +
                env.PathOf("old-data", "images", "student.png").Replace("\\", "\\\\") + "\"}");
            command.Parameters.AddWithValue("$attached", "{\"settings\":{\"ImagePath\":\"" +
                env.PathOf("old-data", "images", "list.png").Replace("\\", "\\\\") + "\"}}");
            command.ExecuteNonQuery();
        }

        Assert.Equal(2, DataRootPathRewriter.RewriteStaging(staging, env.PathOf("old-data"), env.PathOf("new-data")));
        using var verify = new SqliteConnection($"Data Source={databasePath}");
        verify.Open();
        using var read = verify.CreateCommand();
        read.CommandText = "SELECT payload FROM members UNION ALL SELECT payload FROM attached_objects ORDER BY 1";
        using var reader = read.ExecuteReader();
        var payloads = new List<string>();
        while (reader.Read()) payloads.Add(reader.GetString(0));
        Assert.Contains(payloads, value => value.Contains("images/student.png", StringComparison.Ordinal));
        Assert.Contains(payloads, value => value.Contains("images/list.png", StringComparison.Ordinal));
    }

    [Fact]
    public void RewriteStaging_RewritesCentralRosterDatabasePayloads()
    {
        using var env = new DataRootTestEnvironment();
        var staging = env.CreateDirectory("staging");
        var databasePath = env.PathOf("staging", "list", "roll_call.srl");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);" +
                                  "CREATE TABLE profiles (profile_name TEXT PRIMARY KEY);" +
                                  "CREATE TABLE members (profile_name TEXT NOT NULL, ordinal INTEGER NOT NULL, payload TEXT NOT NULL, PRIMARY KEY(profile_name, ordinal));" +
                                  "CREATE TABLE attached_objects (profile_name TEXT NOT NULL, control_id TEXT NOT NULL, payload TEXT NOT NULL, PRIMARY KEY(profile_name, control_id));" +
                                  "INSERT INTO meta VALUES ('format', 'SecRandomRosterDatabase/v2');" +
                                  "INSERT INTO meta VALUES ('schema_version', '2');" +
                                  "INSERT INTO meta VALUES ('kind', 'RollCall');" +
                                  "INSERT INTO profiles VALUES ('class-a');" +
                                  "INSERT INTO members VALUES ('class-a', 0, $member);";
            command.Parameters.AddWithValue("$member", "{\"ImagePath\":\"" +
                env.PathOf("old-data", "images", "student.png").Replace("\\", "\\\\") + "\"}");
            command.ExecuteNonQuery();
        }

        Assert.Equal(1, DataRootPathRewriter.RewriteStaging(staging, env.PathOf("old-data"), env.PathOf("new-data")));
        using var verify = new SqliteConnection($"Data Source={databasePath}");
        verify.Open();
        using var read = verify.CreateCommand();
        read.CommandText = "SELECT payload FROM members";
        Assert.Contains("images/student.png", (string)read.ExecuteScalar()!, StringComparison.Ordinal);
    }

    [Fact]
    public void RewriteRosterDatabase_LeavesOpaqueSqliteByteIdentical()
    {
        using var env = new DataRootTestEnvironment();
        var path = env.WriteFile("opaque.srl", "not sqlite");
        var original = File.ReadAllBytes(path);
        Assert.Equal(0, DataRootPathRewriter.RewriteRosterDatabase(path, env.PathOf("old-data"), env.PathOf("new-data")));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void RewriteStaging_LeavesUnrelatedFilesByteIdentical()
    {
        using var env = new DataRootTestEnvironment();
        var staging = env.CreateDirectory("staging");
        const string original = "{\"unrelated\":\"旧根/" + "not-a-path\"}";
        env.WriteFile("staging/config/settings.json", original);

        DataRootPathRewriter.RewriteStaging(
            staging,
            env.PathOf("old-data"),
            env.PathOf("new-data"));

        Assert.Equal(original, env.ReadFile("staging/config/settings.json"));
    }
}
