using Clothesline.Storage;
using Microsoft.Data.Sqlite;

internal static class ClipboardDefaultsChecks
{
    public static void Run(string parent, Action<bool, string> check)
    {
        foreach (var (name, json, expected) in new[]
        {
            ("missing", "{\"Capacity\":25}", true),
            ("disabled", "{\"Capacity\":25,\"ClipboardEnabled\":false}", false),
            ("enabled", "{\"Capacity\":25,\"ClipboardEnabled\":true}", true)
        })
        {
            var root = Path.Combine(parent, "clipboard-default-" + name);
            using (var store = new LibraryStore(root)) { }
            using (var db = new SqliteConnection($"Data Source={Path.Combine(root, "library.db")};Pooling=False"))
            {
                db.Open(); using var command = db.CreateCommand();
                command.CommandText = "INSERT INTO Settings VALUES(1,$json)";
                command.Parameters.AddWithValue("$json", json); command.ExecuteNonQuery();
            }
            using (var store = new LibraryStore(root))
            {
                var settings = store.LoadSettings();
                check(settings.ClipboardEnabled == expected && settings.Capacity == 25, "clipboard default respects stored choice: " + name);
                store.SaveSettings(settings with { ClipboardEnabled = false });
            }
            using (var store = new LibraryStore(root))
                check(!store.LoadSettings().ClipboardEnabled && store.LoadSettings().Capacity == 25, "manual clipboard disable survives restart: " + name);
        }
    }
}
