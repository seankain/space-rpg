using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Records which chunk scene each baked map image was made from, so drift can
// be detected: the bake tool writes one of these per area
// (Resources/Maps/<Area>/bake_manifest.json) keyed by chunk scene file name
// (e.g. "Chunk_0_0.tscn") to a content hash, and a unit test compares those
// hashes against the current scene files — a mismatch or missing entry means
// the map is stale and the bake needs re-running.
//
// Engine-free on purpose (no Godot types): both the Godot-side baker and the
// xunit test compile this file directly, so they hash identically and can
// never disagree about what "current" means.
public class MapBakeManifest
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public int Version { get; set; } = CurrentVersion;

    // Chunk scene file name -> content hash at bake time.
    public Dictionary<string, string> ChunkHashes { get; set; } = new();

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static MapBakeManifest FromJson(string json)
    {
        var manifest = JsonSerializer.Deserialize<MapBakeManifest>(json, JsonOptions)
            ?? new MapBakeManifest();
        manifest.ChunkHashes ??= new Dictionary<string, string>();
        return manifest;
    }

    // Hash of a chunk scene's text. Newlines are normalized first so a
    // CRLF/LF difference between the machine that baked and the machine that
    // checks (or a git autocrlf checkout) never reads as drift.
    public static string HashSceneText(string sceneText)
    {
        var normalized = sceneText.Replace("\r\n", "\n").Replace("\r", "\n");
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return System.Convert.ToHexString(digest).ToLowerInvariant();
    }
}
