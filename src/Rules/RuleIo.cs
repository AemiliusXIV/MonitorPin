using System.Text.Json;

namespace MonitorPin.Rules;

/// <summary>
/// Import/export of rule sets, so a config can be shared with a friend. Rules
/// keep their monitor match as-is; "any setup" (role) rules travel best, while
/// "this PC" (hardware) rules will show as disconnected until re-pointed.
/// </summary>
public static class RuleIo
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private sealed class RuleSet
    {
        public int Version { get; set; } = 1;
        public List<Rule> Rules { get; set; } = new();
    }

    public const string FileFilter = "MonitorPin rules (*.mprules)|*.mprules|JSON (*.json)|*.json|All files (*.*)|*.*";
    public const string DefaultName = "MonitorPin-rules.mprules";

    public static void Export(string path, IEnumerable<Rule> rules)
    {
        var set = new RuleSet { Rules = rules.ToList() };
        File.WriteAllText(path, JsonSerializer.Serialize(set, Opts));
    }

    /// <summary>Read a rule set. Throws on a malformed file; caller should catch.</summary>
    public static List<Rule> Import(string path)
    {
        var set = JsonSerializer.Deserialize<RuleSet>(File.ReadAllText(path), Opts);
        return set?.Rules ?? new List<Rule>();
    }
}
