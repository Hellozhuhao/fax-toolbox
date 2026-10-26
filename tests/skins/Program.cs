using System.Text.Json;
using System.Text.Json.Nodes;
using FaxToolbox.Windows;

var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var directory = Path.Combine(Path.GetTempPath(), "FaxToolbox-skin-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var path = Path.Combine(directory, "skins.json");
var store = new SkinStore(Path.Combine(root, "apps/windows-shell/ui/skins/builtin.json"), path);
var assertions = 0;
void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
JsonObject Write(JsonObject settings, JsonArray? imported = null)
{
    using var document = JsonDocument.Parse(new JsonObject { ["settings"] = settings, ["imported"] = imported ?? new JsonArray() }.ToJsonString());
    return store.Write(document.RootElement);
}
void Rejected(Action action, string message)
{
    try { action(); } catch (Exception error) when (error is InvalidDataException or InvalidOperationException or JsonException) { assertions++; return; }
    throw new Exception(message);
}
try
{
    var state = store.Read();
    Check(state["skins"]!.AsArray().Count == 3, "Built-in catalog missing");
    var settings = new JsonObject { ["globalId"] = "fax.ocean", ["overrides"] = new JsonObject { ["ai"] = "fax.midnight" } };
    Write(settings);
    Check(SkinStore.Resolve(store.Read(), "ai")["id"]!.GetValue<string>() == "fax.midnight", "Module override not persisted");
    Check(SkinStore.Resolve(store.Read(), "course")["id"]!.GetValue<string>() == "fax.ocean", "Global skin not inherited");
    var imported = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "examples/skins/studio.faxskin")))!.AsObject();
    state = Write(new JsonObject { ["globalId"] = imported["id"]!.GetValue<string>(), ["overrides"] = new JsonObject() }, new JsonArray(imported.DeepClone()));
    Check(state["importedIds"]!.AsArray().Count == 1, "Imported skin missing");
    Check(new SkinStore(Path.Combine(root, "apps/windows-shell/ui/skins/builtin.json"), path).Read()["settings"]!["globalId"]!.GetValue<string>() == "example.studio", "New window lost settings");
    var before = File.ReadAllText(path);
    var invalid = (JsonObject)imported.DeepClone(); invalid["tokens"]!["accent"] = "javascript:alert(1)";
    Rejected(() => Write(new JsonObject { ["globalId"] = "fax.classic", ["overrides"] = new JsonObject() }, new JsonArray(invalid)), "Invalid colors accepted");
    Check(File.ReadAllText(path) == before, "Failed import overwrote the valid configuration");
    Rejected(() => Write(new JsonObject { ["globalId"] = "missing", ["overrides"] = new JsonObject() }), "Unknown skin accepted");
    Rejected(() => Write(new JsonObject { ["globalId"] = "fax.classic", ["overrides"] = new JsonObject() }, new JsonArray(state["skins"]![0]!.DeepClone())), "Built-in ID could be replaced");
    var reset = store.Reset();
    Check(reset["settings"]!["globalId"]!.GetValue<string>() == "fax.classic", "Reset did not restore the default");
    Check(reset["importedIds"]!.AsArray().Count == 1, "Reset removed imported packages");
    File.WriteAllText(path, "broken JSON");
    Check(store.Read()["warning"] is not null, "Corrupted state did not fall back");
    Check(File.ReadAllText(path) == "broken JSON", "Reading corrupted state must not destroy the original");
    Console.WriteLine($"SKIN_STORE_OK {assertions} assertions");
}
finally { Directory.Delete(directory, true); }
