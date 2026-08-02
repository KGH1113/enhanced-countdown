using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using EnhancedCountdown.Launcher;
using EnhancedCountdown.UpdateEngine;

internal static class Program
{
  private static int passed;

  public static int Main()
  {
    try
    {
      Run("manifest validation", TestManifestValidation);
      Run("runtime promotion and trial quarantine", TestRuntimeState);
      Run("stable package installation", TestPackageInstallation);
      Run("checksum and size rejection", TestPackageIntegrity);
      Run("unsafe archive rejection", TestUnsafeArchive);
      Run("stable tag mismatch and URL rejection", TestReleaseValidation);
      Run("beta selection and rejected version filtering", TestBetaAndRejectedVersion);
      Run("operation cancellation", TestCancellation);
      Console.WriteLine($"Updater tests passed: {passed}");
      return 0;
    }
    catch (Exception exception)
    {
      Console.Error.WriteLine(exception);
      return 1;
    }
  }

  private static void TestManifestValidation()
  {
    string valid = Manifest("0.2.0", 10, new string('a', 64));
    Assert(ReleaseManifest.Parse(valid).Version == "0.2.0", "Manifest version was not parsed.");
    AssertThrows<InvalidDataException>(() => ReleaseManifest.Parse(Manifest("0.2.0", 0, new string('a', 64))));
    AssertThrows<InvalidDataException>(() => ReleaseManifest.Parse(Manifest("0.2.0", 10, "bad")));
    AssertThrows<InvalidDataException>(() =>
      ReleaseManifest.Parse(valid.Replace("\"schemaVersion\":1", "\"schemaVersion\":2"))
    );
  }

  private static void TestRuntimeState()
  {
    using TemporaryDirectory temporary = new();
    CreateRuntime(temporary.Path, "0.2.0");
    CreateRuntime(temporary.Path, "0.2.1");
    RuntimeStore store = new(temporary.Path);
    RuntimeState state = new() { Current = "0.2.0", Trial = "0.2.1" };
    store.Save(state);

    RuntimeState repaired = store.LoadAndRepair();
    Assert(repaired.Trial == null, "An abandoned trial was not cleared.");
    Assert(
      repaired.RejectedVersion == "0.2.1" && repaired.FailureCount == 1,
      "An abandoned trial was not quarantined."
    );
    Assert(!Directory.Exists(RuntimePath(temporary.Path, "0.2.1")), "The rejected trial runtime was retained.");

    CreateRuntime(temporary.Path, "0.2.2");
    store.Promote(repaired, "0.2.2");
    Assert(repaired.Current == "0.2.2" && repaired.Previous == "0.2.0", "Runtime promotion did not retain previous.");
    Assert(repaired.RejectedVersion == null && repaired.FailureCount == 0, "Promotion did not clear quarantine.");

    Directory.Delete(RuntimePath(temporary.Path, "0.2.2"), true);
    RuntimeState recovered = store.LoadAndRepair();
    Assert(recovered.Current == "0.2.0" && recovered.Previous == null, "A damaged current runtime did not roll back.");
  }

  private static void TestPackageInstallation()
  {
    using TemporaryDirectory temporary = new();
    byte[] package = CreatePackage("0.3.0", includeUnsafeEntry: false);
    using TestServer server = new();
    server.Manifest = Manifest("0.3.0", package.Length, Sha256(package));
    server.Package = package;
    server.StableRelease = Release("v0.3.0", server.BaseUrl, prerelease: false);

    UpdateResult result = Manager(temporary.Path, server).Resolve("0.2.0");
    Assert(result.Outcome == UpdateOutcomes.Candidate && result.Version == "0.3.0", "Stable update was not selected.");
    Assert(File.Exists(Path.Combine(result.RuntimePath, "EnhancedCountdown.dll")), "Payload was not installed.");
    Assert(
      File.Exists(Path.Combine(result.RuntimePath, "EnhancedCountdown.UpdateEngine.dll")),
      "Update engine was not installed."
    );
  }

  private static void TestPackageIntegrity()
  {
    using TemporaryDirectory temporary = new();
    byte[] package = CreatePackage("0.3.0", includeUnsafeEntry: false);
    using TestServer server = new();
    server.Package = package;
    server.StableRelease = Release("v0.3.0", server.BaseUrl, prerelease: false);

    server.Manifest = Manifest("0.3.0", package.Length, new string('0', 64));
    AssertThrows<InvalidDataException>(() => Manager(temporary.Path, server).Resolve("0.2.0"));
    server.Manifest = Manifest("0.3.0", package.Length + 1, Sha256(package));
    AssertThrows<InvalidDataException>(() => Manager(temporary.Path, server).Resolve("0.2.0"));
  }

  private static void TestUnsafeArchive()
  {
    using TemporaryDirectory temporary = new();
    byte[] package = CreatePackage("0.3.0", includeUnsafeEntry: true);
    using TestServer server = new();
    server.Package = package;
    server.Manifest = Manifest("0.3.0", package.Length, Sha256(package));
    server.StableRelease = Release("v0.3.0", server.BaseUrl, prerelease: false);
    AssertThrows<InvalidDataException>(() => Manager(temporary.Path, server).Resolve("0.2.0"));
    Assert(!File.Exists(Path.Combine(temporary.Path, "escape.txt")), "An unsafe ZIP entry escaped extraction.");
  }

  private static void TestReleaseValidation()
  {
    using TemporaryDirectory temporary = new();
    byte[] package = CreatePackage("0.4.0", includeUnsafeEntry: false);
    using TestServer server = new();
    server.Package = package;
    server.Manifest = Manifest("0.4.0", package.Length, Sha256(package));
    server.StableRelease = Release("v0.3.0", server.BaseUrl, prerelease: false);
    AssertThrows<InvalidDataException>(() => Manager(temporary.Path, server).Resolve("0.2.0"));

    server.StableRelease = new
    {
      tag_name = "v0.4.0",
      draft = false,
      prerelease = false,
      assets = new[]
      {
        new { name = UpdateManager.ManifestAsset, browser_download_url = "https://evil.example/manifest" },
        new { name = UpdateManager.PackageAsset, browser_download_url = "https://evil.example/package" },
      },
    };
    AssertThrows<InvalidDataException>(() => Manager(temporary.Path, server).Resolve("0.2.0"));
  }

  private static void TestBetaAndRejectedVersion()
  {
    using TemporaryDirectory temporary = new();
    Directory.CreateDirectory(Path.Combine(temporary.Path, "Runtime"));
    File.WriteAllText(Path.Combine(temporary.Path, "UpdateSettings.json"), "{\"ReceiveBetaUpdates\":true}");
    File.WriteAllText(Path.Combine(temporary.Path, "Runtime", "state.json"), "{\"RejectedVersion\":\"0.4.0-beta.2\"}");
    byte[] package = CreatePackage("0.4.0-beta.1", includeUnsafeEntry: false);
    using TestServer server = new();
    server.Package = package;
    server.Manifest = Manifest("0.4.0-beta.1", package.Length, Sha256(package));
    server.Releases = new[]
    {
      Release("v0.4.0-beta.2", server.BaseUrl, prerelease: true),
      Release("v0.4.0-beta.1", server.BaseUrl, prerelease: true),
      Release("v9.0.0", server.BaseUrl, prerelease: false, draft: true),
    };

    UpdateResult result = Manager(temporary.Path, server).Resolve("0.3.0");
    Assert(result.Version == "0.4.0-beta.1", "The rejected beta was not skipped in favor of the next release.");
  }

  private static void TestCancellation()
  {
    using TemporaryDirectory temporary = new();
    using TestServer server = new() { DelayMilliseconds = 500 };
    UpdateManager manager = new(
      temporary.Path,
      server.BaseUrl + "stable",
      server.BaseUrl + "releases",
      allowLocalTestUrls: true,
      operationTimeout: TimeSpan.FromMilliseconds(50)
    );
    AssertThrows<TimeoutException>(() => manager.Resolve("0.2.0"));
  }

  private static UpdateManager Manager(string installPath, TestServer server)
  {
    return new UpdateManager(installPath, server.BaseUrl + "stable", server.BaseUrl + "releases");
  }

  private static object Release(string tag, string baseUrl, bool prerelease, bool draft = false)
  {
    return new
    {
      tag_name = tag,
      draft,
      prerelease,
      assets = new[]
      {
        new { name = UpdateManager.ManifestAsset, browser_download_url = baseUrl + UpdateManager.ManifestAsset },
        new { name = UpdateManager.PackageAsset, browser_download_url = baseUrl + UpdateManager.PackageAsset },
      },
    };
  }

  private static byte[] CreatePackage(string version, bool includeUnsafeEntry)
  {
    using MemoryStream buffer = new();
    using (ZipArchive archive = new(buffer, ZipArchiveMode.Create, true))
    {
      string root = $"EnhancedCountdown/Runtime/versions/{version}/";
      AddEntry(archive, root + "EnhancedCountdown.dll", "payload");
      AddEntry(archive, root + "EnhancedCountdown.UpdateEngine.dll", "engine");
      AddEntry(archive, root + "Info.json", $"{{\"Version\":\"{version}\"}}");
      if (includeUnsafeEntry)
        AddEntry(archive, "../escape.txt", "unsafe");
    }
    return buffer.ToArray();
  }

  private static void AddEntry(ZipArchive archive, string path, string content)
  {
    ZipArchiveEntry entry = archive.CreateEntry(path);
    using StreamWriter writer = new(entry.Open(), Encoding.UTF8);
    writer.Write(content);
  }

  private static string Manifest(string version, long bytes, string checksum)
  {
    return $"{{\"schemaVersion\":1,\"version\":\"{version}\",\"packageAsset\":\"EnhancedCountdown.zip\",\"packageBytes\":{bytes},\"packageSha256\":\"{checksum}\",\"runtimePath\":\"EnhancedCountdown/Runtime/versions/{version}\"}}";
  }

  private static string Sha256(byte[] bytes)
  {
    return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
  }

  private static void CreateRuntime(string installPath, string version)
  {
    string runtime = RuntimePath(installPath, version);
    Directory.CreateDirectory(runtime);
    File.WriteAllText(Path.Combine(runtime, "EnhancedCountdown.dll"), "payload");
    File.WriteAllText(Path.Combine(runtime, "EnhancedCountdown.UpdateEngine.dll"), "engine");
    File.WriteAllText(Path.Combine(runtime, "Info.json"), $"{{\"Version\":\"{version}\"}}");
  }

  private static string RuntimePath(string installPath, string version)
  {
    return Path.Combine(installPath, "Runtime", "versions", version);
  }

  private static void Run(string name, Action test)
  {
    test();
    passed++;
    Console.WriteLine("PASS " + name);
  }

  private static void Assert(bool condition, string message)
  {
    if (!condition)
      throw new InvalidOperationException(message);
  }

  private static void AssertThrows<T>(Action action)
    where T : Exception
  {
    try
    {
      action();
    }
    catch (T)
    {
      return;
    }
    throw new InvalidOperationException("Expected exception: " + typeof(T).Name);
  }
}

internal sealed class TemporaryDirectory : IDisposable
{
  public TemporaryDirectory()
  {
    Path = System.IO.Path.Combine(
      System.IO.Path.GetTempPath(),
      "enhanced-countdown-tests-" + Guid.NewGuid().ToString("N")
    );
    Directory.CreateDirectory(Path);
  }

  public string Path { get; }

  public void Dispose()
  {
    try
    {
      Directory.Delete(Path, true);
    }
    catch { }
  }
}

internal sealed class TestServer : IDisposable
{
  private readonly HttpListener listener = new();
  private readonly Thread worker;

  public TestServer()
  {
    using TcpListener reservation = new(IPAddress.Loopback, 0);
    reservation.Start();
    int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
    reservation.Stop();
    BaseUrl = $"http://127.0.0.1:{port}/";
    listener.Prefixes.Add(BaseUrl);
    listener.Start();
    worker = new Thread(Serve) { IsBackground = true };
    worker.Start();
  }

  public string BaseUrl { get; }
  public object StableRelease { get; set; }
  public object[] Releases { get; set; } = Array.Empty<object>();
  public string Manifest { get; set; } = "{}";
  public byte[] Package { get; set; } = Array.Empty<byte>();
  public int DelayMilliseconds { get; set; }

  public void Dispose()
  {
    listener.Close();
    worker.Join(TimeSpan.FromSeconds(1));
  }

  private void Serve()
  {
    while (listener.IsListening)
    {
      try
      {
        HttpListenerContext context = listener.GetContext();
        if (DelayMilliseconds > 0)
          Thread.Sleep(DelayMilliseconds);
        byte[] response = ResponseFor(context.Request.Url.AbsolutePath);
        context.Response.ContentLength64 = response.Length;
        context.Response.OutputStream.Write(response, 0, response.Length);
        context.Response.Close();
      }
      catch when (!listener.IsListening)
      {
        return;
      }
      catch
      {
        // Individual client cancellations must not stop the test server.
      }
    }
  }

  private byte[] ResponseFor(string path)
  {
    if (path.EndsWith("/stable", StringComparison.Ordinal))
      return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(StableRelease));
    if (path.EndsWith("/releases", StringComparison.Ordinal))
      return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(Releases));
    if (path.EndsWith("/" + UpdateManager.ManifestAsset, StringComparison.Ordinal))
      return Encoding.UTF8.GetBytes(Manifest);
    if (path.EndsWith("/" + UpdateManager.PackageAsset, StringComparison.Ordinal))
      return Package;
    return Array.Empty<byte>();
  }
}
