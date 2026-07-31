using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace RemoveCountdown.UpdateEngine;

internal sealed class UpdateManager
{
  private const string StableReleaseApiUrl = "https://api.github.com/repos/KGH1113/enhanced-countdown/releases/latest";
  private const string ReleasesApiUrl = "https://api.github.com/repos/KGH1113/enhanced-countdown/releases?per_page=100";
  internal const string ManifestAsset = "RemoveCountdown.update.json";
  internal const string PackageAsset = "RemoveCountdown.zip";
  internal const long MaximumPackageBytes = 128L * 1024 * 1024;
  private const long MaximumExtractedBytes = 256L * 1024 * 1024;
  private static readonly TimeSpan NetworkTimeout = TimeSpan.FromSeconds(20);
  private static readonly Regex VersionPattern = new(
    "\\\"Version\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"",
    RegexOptions.CultureInvariant
  );

  private readonly string installPath;
  private readonly string runtimeRoot;
  private readonly string versionsRoot;
  private readonly string preferencesPath;
  private readonly string stableReleaseApiUrl;
  private readonly string releasesApiUrl;
  private readonly bool allowLocalTestUrls;
  private readonly TimeSpan operationTimeout;

  public UpdateManager(string installPath)
    : this(installPath, StableReleaseApiUrl, ReleasesApiUrl, false, NetworkTimeout) { }

  internal UpdateManager(
    string installPath,
    string stableReleaseApiUrl,
    string releasesApiUrl,
    bool allowLocalTestUrls = true,
    TimeSpan? operationTimeout = null
  )
  {
    this.installPath = Path.GetFullPath(installPath ?? throw new ArgumentNullException(nameof(installPath)));
    runtimeRoot = Path.Combine(this.installPath, "Runtime");
    versionsRoot = Path.Combine(runtimeRoot, "versions");
    preferencesPath = Path.Combine(this.installPath, "UpdateSettings.json");
    this.stableReleaseApiUrl = stableReleaseApiUrl;
    this.releasesApiUrl = releasesApiUrl;
    this.allowLocalTestUrls = allowLocalTestUrls;
    this.operationTimeout = operationTimeout ?? NetworkTimeout;
  }

  public UpdateResult Resolve(string currentVersion)
  {
    CleanupTemporaryArtifacts();
    using CancellationTokenSource timeout = new(operationTimeout);
    try
    {
      return ResolveAsync(currentVersion, timeout.Token).GetAwaiter().GetResult();
    }
    catch (OperationCanceledException) when (timeout.IsCancellationRequested)
    {
      throw new TimeoutException("RemoveCountdown update operations timed out.");
    }
  }

  private async Task<UpdateResult> ResolveAsync(string currentVersion, CancellationToken cancellationToken)
  {
    SemanticVersion current = SemanticVersion.Parse(currentVersion);
    string rejectedVersion = ReadRejectedVersion();
    using HttpClient client = CreateClient();
    ReleaseAssets release = await ResolveReleaseAsync(client, rejectedVersion, cancellationToken).ConfigureAwait(false);
    if (release == null)
      return new UpdateResult { Outcome = UpdateOutcomes.None };

    ReleaseManifest manifest = ReleaseManifest.Parse(
      await DownloadTextAsync(client, release.ManifestUrl, 16 * 1024, cancellationToken).ConfigureAwait(false)
    );
    SemanticVersion available = SemanticVersion.Parse(manifest.Version);
    if (available.CompareTo(SemanticVersion.Parse(release.ExpectedVersion)) != 0)
      throw new InvalidDataException("The release tag and update manifest versions do not match.");
    if (available.CompareTo(current) <= 0 || VersionsEqual(manifest.Version, rejectedVersion))
      return new UpdateResult { Outcome = UpdateOutcomes.None };

    string existing = GetVersionDirectory(manifest.Version);
    if (TryValidateRuntime(existing, manifest.Version))
    {
      return new UpdateResult
      {
        Outcome = UpdateOutcomes.Candidate,
        Version = manifest.Version,
        RuntimePath = existing,
      };
    }

    Directory.CreateDirectory(runtimeRoot);
    string packagePath = Path.Combine(runtimeRoot, "download-" + Guid.NewGuid().ToString("N") + ".zip");
    try
    {
      await DownloadFileAsync(client, release.PackageUrl, packagePath, manifest.PackageBytes, cancellationToken)
        .ConfigureAwait(false);
      VerifyChecksum(packagePath, manifest.PackageSha256, cancellationToken);
      string runtimePath = InstallPackage(packagePath, manifest, cancellationToken);
      return new UpdateResult
      {
        Outcome = UpdateOutcomes.Candidate,
        Version = manifest.Version,
        RuntimePath = runtimePath,
      };
    }
    finally
    {
      TryDeleteFile(packagePath);
    }
  }

  private async Task<ReleaseAssets> ResolveReleaseAsync(
    HttpClient client,
    string rejectedVersion,
    CancellationToken cancellationToken
  )
  {
    if (!UpdatePreferences.Load(preferencesPath).ReceiveBetaUpdates)
    {
      string stableJson = await DownloadTextAsync(client, stableReleaseApiUrl, 1024 * 1024, cancellationToken)
        .ConfigureAwait(false);
      JObject stable = JObject.Parse(stableJson);
      if (stable.Value<bool?>("draft") == true || stable.Value<bool?>("prerelease") == true)
        throw new InvalidDataException("The latest stable GitHub release is not a published stable release.");
      string tag = stable.Value<string>("tag_name");
      if (VersionsEqual(tag, rejectedVersion))
        return null;
      return ReadReleaseAssets(tag, stable["assets"] as JArray)
        ?? throw new InvalidDataException("The latest stable release does not contain the required update assets.");
    }

    string response = await DownloadTextAsync(client, releasesApiUrl, 8 * 1024 * 1024, cancellationToken)
      .ConfigureAwait(false);
    ReleaseAssets selected = null;
    SemanticVersion selectedVersion = null;
    foreach (JObject release in JArray.Parse(response).OfType<JObject>())
    {
      if (release.Value<bool?>("draft") == true)
        continue;
      string tag = release.Value<string>("tag_name");
      if (!SemanticVersion.TryParse(tag, out SemanticVersion version) || VersionsEqual(tag, rejectedVersion))
        continue;
      ReleaseAssets assets = ReadReleaseAssets(tag, release["assets"] as JArray);
      if (assets == null || selectedVersion != null && version.CompareTo(selectedVersion) <= 0)
        continue;
      selected = assets;
      selectedVersion = version;
    }
    return selected;
  }

  private ReleaseAssets ReadReleaseAssets(string version, JArray assets)
  {
    if (!SemanticVersion.TryParse(version, out _))
      return null;
    string manifestUrl = null;
    string packageUrl = null;
    foreach (JObject asset in assets?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
    {
      string name = asset.Value<string>("name");
      string url = asset.Value<string>("browser_download_url");
      if (!IsTrustedReleaseUrl(url))
        continue;
      if (name == ManifestAsset)
        manifestUrl = url;
      else if (name == PackageAsset)
        packageUrl = url;
    }
    return manifestUrl != null && packageUrl != null ? new ReleaseAssets(version, manifestUrl, packageUrl) : null;
  }

  private string InstallPackage(string packagePath, ReleaseManifest manifest, CancellationToken cancellationToken)
  {
    string extractionRoot = Path.Combine(runtimeRoot, "extract-" + Guid.NewGuid().ToString("N"));
    string target = GetVersionDirectory(manifest.Version);
    try
    {
      ExtractPackage(packagePath, extractionRoot, cancellationToken);
      string source = ResolveContainedPath(extractionRoot, manifest.RuntimePath);
      ValidateRuntime(source, manifest.Version);
      cancellationToken.ThrowIfCancellationRequested();
      Directory.CreateDirectory(versionsRoot);
      if (Directory.Exists(target))
        Directory.Delete(target, true);
      cancellationToken.ThrowIfCancellationRequested();
      Directory.Move(source, target);
      return target;
    }
    finally
    {
      TryDeleteDirectory(extractionRoot);
    }
  }

  private static void ExtractPackage(string packagePath, string destinationRoot, CancellationToken cancellationToken)
  {
    Directory.CreateDirectory(destinationRoot);
    string rootPrefix = EnsureTrailingSeparator(Path.GetFullPath(destinationRoot));
    using FileStream package = File.OpenRead(packagePath);
    using ZipArchive archive = new(package, ZipArchiveMode.Read);
    long extractedBytes = 0;
    byte[] buffer = new byte[81920];
    foreach (ZipArchiveEntry entry in archive.Entries)
    {
      cancellationToken.ThrowIfCancellationRequested();
      extractedBytes = checked(extractedBytes + entry.Length);
      if (extractedBytes > MaximumExtractedBytes)
        throw new InvalidDataException("The extracted update package is too large.");
      string destinationPath = Path.GetFullPath(
        Path.Combine(destinationRoot, entry.FullName.Replace('/', Path.DirectorySeparatorChar))
      );
      if (!destinationPath.StartsWith(rootPrefix, StringComparison.Ordinal))
        throw new InvalidDataException("The update package contains an unsafe path.");
      if (string.IsNullOrEmpty(entry.Name))
      {
        Directory.CreateDirectory(destinationPath);
        continue;
      }

      Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
      using Stream source = entry.Open();
      using FileStream destination = new(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
      long copied = CopyStream(source, destination, buffer, cancellationToken);
      if (copied != entry.Length)
        throw new InvalidDataException("The extracted update entry size is invalid.");
      RestoreUnixPermissions(entry, destinationPath);
    }
  }

  private static long CopyStream(Stream source, Stream destination, byte[] buffer, CancellationToken cancellationToken)
  {
    long total = 0;
    while (true)
    {
      cancellationToken.ThrowIfCancellationRequested();
      int read = source.Read(buffer, 0, buffer.Length);
      if (read == 0)
        return total;
      total = checked(total + read);
      destination.Write(buffer, 0, read);
    }
  }

  private string GetVersionDirectory(string version)
  {
    return Path.Combine(versionsRoot, SemanticVersion.Parse(version).ToString());
  }

  private static bool TryValidateRuntime(string directory, string version)
  {
    try
    {
      ValidateRuntime(directory, version);
      return true;
    }
    catch
    {
      return false;
    }
  }

  private static void ValidateRuntime(string directory, string expectedVersion)
  {
    string assemblyPath = Path.Combine(directory, "RemoveCountdown.dll");
    string enginePath = Path.Combine(directory, "RemoveCountdown.UpdateEngine.dll");
    string infoPath = Path.Combine(directory, "Info.json");
    if (!File.Exists(assemblyPath) || !File.Exists(enginePath) || !File.Exists(infoPath))
      throw new InvalidDataException("The update package does not contain a complete runtime.");
    Match match = VersionPattern.Match(File.ReadAllText(infoPath));
    if (
      !match.Success
      || SemanticVersion.Parse(match.Groups[1].Value).CompareTo(SemanticVersion.Parse(expectedVersion)) != 0
    )
      throw new InvalidDataException("The packaged runtime version does not match the update manifest.");
  }

  private void CleanupTemporaryArtifacts()
  {
    if (!Directory.Exists(runtimeRoot))
      return;
    foreach (string file in Directory.GetFiles(runtimeRoot, "download-*.zip"))
      TryDeleteFile(file);
    foreach (string directory in Directory.GetDirectories(runtimeRoot, "extract-*"))
      TryDeleteDirectory(directory);
  }

  private static string ResolveContainedPath(string root, string relativePath)
  {
    string fullRoot = EnsureTrailingSeparator(Path.GetFullPath(root));
    string fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    if (!fullPath.StartsWith(fullRoot, StringComparison.Ordinal))
      throw new InvalidDataException("The update manifest runtime path escapes the package.");
    return fullPath;
  }

  private bool IsTrustedReleaseUrl(string value)
  {
    if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri))
      return false;
    if (allowLocalTestUrls && uri.IsLoopback && uri.Scheme == Uri.UriSchemeHttp)
      return true;
    return uri.Scheme == Uri.UriSchemeHttps
      && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase);
  }

  private static HttpClient CreateClient()
  {
    HttpClient client = new() { Timeout = Timeout.InfiniteTimeSpan };
    client.DefaultRequestHeaders.UserAgent.ParseAdd("RemoveCountdown-AutoUpdater/1.0");
    return client;
  }

  private static async Task<string> DownloadTextAsync(
    HttpClient client,
    string url,
    int maximumBytes,
    CancellationToken cancellationToken
  )
  {
    using HttpResponseMessage response = await client
      .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
      .ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
    using Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
    using MemoryStream buffer = new();
    await CopyWithLimitAsync(stream, buffer, maximumBytes, cancellationToken).ConfigureAwait(false);
    return Encoding.UTF8.GetString(buffer.ToArray());
  }

  private static async Task DownloadFileAsync(
    HttpClient client,
    string url,
    string destinationPath,
    long expectedBytes,
    CancellationToken cancellationToken
  )
  {
    using HttpResponseMessage response = await client
      .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
      .ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
    if (
      response.Content.Headers.ContentLength.HasValue
      && response.Content.Headers.ContentLength.Value != expectedBytes
    )
      throw new InvalidDataException("The update package size does not match its manifest.");
    using Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
    using FileStream destination = new(
      destinationPath,
      FileMode.CreateNew,
      FileAccess.Write,
      FileShare.None,
      81920,
      true
    );
    long copied = await CopyWithLimitAsync(source, destination, MaximumPackageBytes, cancellationToken)
      .ConfigureAwait(false);
    if (copied != expectedBytes)
      throw new InvalidDataException("The downloaded package size does not match its manifest.");
  }

  private static async Task<long> CopyWithLimitAsync(
    Stream source,
    Stream destination,
    long maximumBytes,
    CancellationToken cancellationToken
  )
  {
    byte[] buffer = new byte[81920];
    long total = 0;
    while (true)
    {
      int read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
      if (read == 0)
        return total;
      total = checked(total + read);
      if (total > maximumBytes)
        throw new InvalidDataException("The downloaded update asset is too large.");
      await destination.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
    }
  }

  private static void VerifyChecksum(string path, string expectedChecksum, CancellationToken cancellationToken)
  {
    using SHA256 sha256 = SHA256.Create();
    using FileStream stream = File.OpenRead(path);
    byte[] buffer = new byte[81920];
    while (true)
    {
      cancellationToken.ThrowIfCancellationRequested();
      int read = stream.Read(buffer, 0, buffer.Length);
      if (read == 0)
        break;
      sha256.TransformBlock(buffer, 0, read, null, 0);
    }
    sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
    string actual = string.Concat(sha256.Hash.Select(value => value.ToString("x2")));
    if (!string.Equals(actual, expectedChecksum, StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException("The update package checksum does not match its manifest.");
  }

  private string ReadRejectedVersion()
  {
    try
    {
      string path = Path.Combine(runtimeRoot, "state.json");
      if (!File.Exists(path))
        return null;
      return JObject.Parse(File.ReadAllText(path)).Value<string>("RejectedVersion");
    }
    catch
    {
      return null;
    }
  }

  private static bool VersionsEqual(string left, string right)
  {
    return SemanticVersion.TryParse(left, out SemanticVersion leftVersion)
      && SemanticVersion.TryParse(right, out SemanticVersion rightVersion)
      && leftVersion.CompareTo(rightVersion) == 0;
  }

  private static string EnsureTrailingSeparator(string path)
  {
    return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
      ? path
      : path + Path.DirectorySeparatorChar;
  }

  private static void RestoreUnixPermissions(ZipArchiveEntry entry, string destinationPath)
  {
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
      return;
    int mode = (entry.ExternalAttributes >> 16) & 0x1FF;
    if (mode != 0)
      Chmod(destinationPath, (uint)mode);
  }

  [DllImport("libc", EntryPoint = "chmod", SetLastError = true)]
  private static extern int Chmod(string path, uint mode);

  private static void TryDeleteFile(string path)
  {
    try
    {
      if (File.Exists(path))
        File.Delete(path);
    }
    catch { }
  }

  private static void TryDeleteDirectory(string path)
  {
    try
    {
      if (Directory.Exists(path))
        Directory.Delete(path, true);
    }
    catch { }
  }
}
