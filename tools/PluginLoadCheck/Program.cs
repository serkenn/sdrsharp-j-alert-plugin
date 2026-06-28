// Integration test: reproduce SDR#'s plugin-discovery against the REAL SDR#
// assemblies, to prove the built plugin actually loads and is recognized.
//
// Mirrors SDRSharp.MainForm.LoadPluginTree: Assembly.LoadFrom(dll), scan
// GetExportedTypes() for a non-abstract type with a public parameterless ctor
// that implements SDRSharp.Common.ISharpPlugin, instantiate it, and read
// DisplayName.
//
// Usage: PluginLoadCheck <plugin.dll> <dir-with-real-SDRSharp-DLLs>
// Exit code 0 = a usable ISharpPlugin was found and instantiated; 1 = not found.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: PluginLoadCheck <plugin.dll> <real-dll-dir>");
            return 2;
        }
        string pluginPath = Path.GetFullPath(args[0]);
        string realDir = Path.GetFullPath(args[1]);

        // Resolve SDRSharp.* (and any other) dependencies from the real SDR#
        // assembly directory, so the plugin binds to the host's real types.
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            string candidate = Path.Combine(realDir, name.Name + ".dll");
            return File.Exists(candidate) ? ctx.LoadFromAssemblyPath(candidate) : null;
        };

        Assembly asm = Assembly.LoadFrom(pluginPath);
        Type[] types = asm.GetExportedTypes();

        Type pluginType = types.FirstOrDefault(t =>
            !t.IsAbstract &&
            t.GetConstructor(Type.EmptyTypes) != null &&
            t.GetInterfaces().Any(i => i.FullName == "SDRSharp.Common.ISharpPlugin"));

        if (pluginType == null)
        {
            Console.Error.WriteLine("FAIL: no type implementing SDRSharp.Common.ISharpPlugin " +
                                    "with a public parameterless constructor was found.");
            Console.Error.WriteLine("Exported types:");
            foreach (Type t in types) Console.Error.WriteLine("  " + t.FullName);
            return 1;
        }

        object instance = Activator.CreateInstance(pluginType);
        string displayName = (string)pluginType.GetProperty("DisplayName")?.GetValue(instance);
        Console.WriteLine($"PASS: discovered & instantiated {pluginType.FullName}");
        Console.WriteLine($"      DisplayName = \"{displayName}\"");
        return 0;
    }
}
