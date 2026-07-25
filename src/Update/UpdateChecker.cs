using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace MonitorPin.Update;

public sealed class UpdateInfo
{
    public required Version Version { get; init; }
    public required string Tag { get; init; }
    public required string Notes { get; init; }
    public required string DownloadUrl { get; init; }
    public required string AssetName { get; init; }
    public required long Size { get; init; }
    public required string ReleasePage { get; init; }
    /// <summary>Expected SHA-256 from the release's SHA256SUMS.txt, when published.</summary>
    public string? Sha256 { get; init; }
}

/// <summary>
/// Checks GitHub Releases for a newer version and downloads the installer.
///
/// Deliberately narrow for safety: the API endpoint and the download host are
/// pinned to this one repo, and we never follow a URL handed to us by the
/// response body without validating it first. If the release publishes a
/// SHA256SUMS.txt we verify the installer against it before running anything.
/// </summary>
public static class UpdateChecker
{
    public const string Owner = "AemiliusXIV";
    public const string Repo = "MonitorPin";

    private const string ApiUrl = "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest";
    private const string SumsAsset = "SHA256SUMS.txt";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("MonitorPin-UpdateCheck");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    /// <summary>Version of the running app, normalized to Major.Minor.Patch.</summary>
    public static Version CurrentVersion
        => Normalize(typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0));

    /// <summary>Null when up to date, unreachable, or the release looks wrong.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        string json = await Http.GetStringAsync(ApiUrl, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (Bool(root, "draft") || Bool(root, "prerelease")) return null;

        string tag = Str(root, "tag_name") ?? "";
        var version = ParseTag(tag);
        if (version == null || version <= CurrentVersion) return null;

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        string? url = null, name = null, sumsUrl = null;
        long size = 0;
        foreach (var a in assets.EnumerateArray())
        {
            string? an = Str(a, "name");
            string? au = Str(a, "browser_download_url");
            if (an == null || au == null || !IsTrustedDownload(au)) continue;

            if (an.Equals(SumsAsset, StringComparison.OrdinalIgnoreCase)) { sumsUrl = au; continue; }
            if (an.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                an.Contains("setup", StringComparison.OrdinalIgnoreCase))
            {
                url = au; name = an;
                size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var sv) ? sv : 0;
            }
        }
        if (url == null || name == null) return null;

        string? sha = sumsUrl != null ? await TryGetSha(sumsUrl, name, ct).ConfigureAwait(false) : null;

        return new UpdateInfo
        {
            Version = version,
            Tag = tag,
            Notes = Str(root, "body") ?? "",
            DownloadUrl = url,
            AssetName = name,
            Size = size,
            ReleasePage = Str(root, "html_url") ?? $"https://github.com/{Owner}/{Repo}/releases/latest",
            Sha256 = sha,
        };
    }

    /// <summary>
    /// Download the installer to a temp file and verify it. Returns the path.
    /// Throws if the URL isn't ours or the checksum doesn't match.
    /// </summary>
    public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<int>? progress, CancellationToken ct = default)
    {
        if (!IsTrustedDownload(info.DownloadUrl))
            throw new InvalidOperationException("Refusing to download from an unexpected location.");

        string dir = Path.Combine(Path.GetTempPath(), "MonitorPinUpdate");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, info.AssetName);

        using (var resp = await Http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? info.Size;
            await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = File.Create(path);

            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                read += n;
                if (total > 0) progress?.Report((int)(read * 100 / total));
            }
        }

        if (info.Sha256 != null)
        {
            string actual = Sha256File(path);
            if (!actual.Equals(info.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(path); } catch { }
                throw new InvalidOperationException("The downloaded file failed its checksum check, so it was discarded.");
            }
        }
        return path;
    }

    /// <summary>Only https://github.com/<owner>/<repo>/... is acceptable.</summary>
    private static bool IsTrustedDownload(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var u)
           && u.Scheme == Uri.UriSchemeHttps
           && u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
           && u.AbsolutePath.StartsWith($"/{Owner}/{Repo}/", StringComparison.OrdinalIgnoreCase);

    private static async Task<string?> TryGetSha(string sumsUrl, string assetName, CancellationToken ct)
    {
        try
        {
            string text = await Http.GetStringAsync(sumsUrl, ct).ConfigureAwait(false);
            foreach (var line in text.Split('\n'))
            {
                // "<hash>  <filename>"
                var parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[^1].TrimStart('*').Equals(assetName, StringComparison.OrdinalIgnoreCase))
                    return parts[0];
            }
        }
        catch { }
        return null;
    }

    private static string Sha256File(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs));
    }

    public static Version? ParseTag(string tag)
    {
        string t = tag.Trim();
        if (t.StartsWith("v", StringComparison.OrdinalIgnoreCase)) t = t[1..];
        return Version.TryParse(t, out var v) ? Normalize(v) : null;
    }

    /// <summary>Tags are "0.2.0" while assemblies are "0.2.0.0"; compare on 3 parts.</summary>
    public static Version Normalize(Version v)
        => new(v.Major, v.Minor, v.Build < 0 ? 0 : v.Build);

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static bool Bool(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
}
