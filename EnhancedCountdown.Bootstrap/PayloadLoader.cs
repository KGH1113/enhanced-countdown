using System;
using System.IO;
using System.Reflection;
using UnityModManagerNet;

namespace EnhancedCountdown.Launcher;

internal static class PayloadLoader
{
  private static readonly object Sync = new();
  private static string payloadDirectory;
  private static bool resolverRegistered;

  public static void Load(string assemblyPath, string entryMethod, UnityModManager.ModEntry modEntry)
  {
    if (!File.Exists(assemblyPath))
      throw new FileNotFoundException("EnhancedCountdown payload was not found.", assemblyPath);

    int separator = entryMethod.LastIndexOf('.');
    if (separator <= 0 || separator == entryMethod.Length - 1)
      throw new InvalidDataException("EntryMethod must contain a type and method name.");

    ConfigureResolver(Path.GetDirectoryName(Path.GetFullPath(assemblyPath)));
    Assembly assembly = Assembly.LoadFrom(Path.GetFullPath(assemblyPath));
    string typeName = entryMethod.Substring(0, separator);
    string methodName = entryMethod.Substring(separator + 1);
    Type type = assembly.GetType(typeName, true);
    MethodInfo method =
      type.GetMethod(
        methodName,
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
        null,
        new[] { typeof(UnityModManager.ModEntry) },
        null
      ) ?? throw new MissingMethodException(typeName, methodName);

    try
    {
      object result = method.Invoke(null, new object[] { modEntry });
      if (method.ReturnType == typeof(bool) && result is bool loaded && !loaded)
        throw new InvalidOperationException(entryMethod + " returned false.");
    }
    catch (TargetInvocationException exception) when (exception.InnerException != null)
    {
      throw exception.InnerException;
    }
  }

  private static void ConfigureResolver(string directory)
  {
    lock (Sync)
    {
      payloadDirectory = directory;
      if (resolverRegistered)
        return;
      AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
      resolverRegistered = true;
    }
  }

  private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
  {
    string directory;
    lock (Sync)
      directory = payloadDirectory;
    if (string.IsNullOrWhiteSpace(directory))
      return null;

    string name = new AssemblyName(args.Name).Name;
    string candidate = Path.Combine(directory, name + ".dll");
    return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
  }
}
