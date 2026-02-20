global using static MyMeteor.IO.MyDirectoryTaxi;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace MyMeteor.IO;

/// <summary>
/// A utility for the smooth management of working directories.
/// </summary>
/// <remarks>Global using within the MyMeteor.IO namespace. (Thanks to Taichi Suzuki for the inspiration.)</remarks>
public class MyDirectoryTaxi
{
    private static readonly ConcurrentDictionary<int, Stack<string>> TaskStacks = [];
    private static Stack<string> Staxi
    {
        get
        {
            if (!TaskStacks.ContainsKey(Task.CurrentId ?? -1)) TaskStacks[Task.CurrentId ?? -1] = [];
            return TaskStacks[Task.CurrentId ?? -1];
        }
    }

    private static bool SuppressWarnings = false;

    /// <summary>
    /// Push the current working directory onto the stack.
    /// </summary>
    public static void PushDirectory() => Staxi.Push(Environment.CurrentDirectory);

    /// <summary>
    /// Push a path onto the stack.
    /// </summary>
    public static void PushDirectory(string path) => Staxi.Push(path);

    /// <summary>
    /// Pop a path from the stack.
    /// </summary>
    public static void PopDirectory()
    {
        if (Staxi.Count > 0)
            Staxi.Pop();
    }

    internal static void InternalPushDirectoryAndGo(string path)
    {
        Staxi.Push(Environment.CurrentDirectory);
        Directory.SetCurrentDirectory(path);
    }

    /// <summary>
    /// Push the current working directory to the stack and move to a new directory, creating it if it does not exist.
    /// </summary>
    /// <param name="path">The new directory.</param>
    public static void CreateDirectoryPushAndGo(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && path != "." && path != "..")
        {
            Directory.CreateDirectory(path);
            InternalPushDirectoryAndGo(path);
        }

        else if (path == null)
        {
            if (!SuppressWarnings)
                Console.WriteLine("WARNING: Null string provided to CreateDirectoryPushAndGo. No work was performed.");
        }

        else if (path == "..")
            PushDirectoryAndGoUp();

        else
        {
            Staxi.Push(Environment.CurrentDirectory);
            if (!SuppressWarnings)
                Console.WriteLine($"WARNING: {path} provided to CreateDirectoryPushAndGo. The current directory was pushed onto the stack.");
        }
    }

    /// <summary>
    /// Push the current working directory to the stack and move to a new working directory.
    /// </summary>
    /// <param name="path">The new directory.</param>
    /// <remarks>Throws an exception if the requested directory does not exist.</remarks>
    /// <exception cref="DirectoryNotFoundException"></exception>
    public static void PushDirectoryAndGo(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && path != "." && path != "..")
        {
            if (Path.Exists(path))
                InternalPushDirectoryAndGo(path);

            else
                throw new DirectoryNotFoundException();
        }

        else if (path == null)
        {
            if (!SuppressWarnings)
                Console.WriteLine("WARNING: Null string provided to PushDirectoryAndGo. No work was performed.");
        }

        else if (path == "..")
            PushDirectoryAndGoUp();

        else
        {
            Staxi.Push(Environment.CurrentDirectory);
            if (!SuppressWarnings)
                Console.WriteLine($"WARNING: {path} provided to PushDirectoryAndGo. The current directory was pushed onto the stack.");
        }
    }

    /// <summary>
    /// Pop a directory from the stack and set the current working directory to it.
    /// </summary>
    public static void PopDirectoryAndGo()
    {
        if (Staxi.Count > 0)
            Directory.SetCurrentDirectory(Staxi.Pop());
    }

    /// <summary>
    /// Push the current working directory to the stack,
    /// then set the current directory to its containing directory if possible.
    /// </summary>
    public static void PushDirectoryAndGoUp()
    {
        string backOne = Path.GetDirectoryName(Environment.CurrentDirectory);

        if (string.IsNullOrEmpty(backOne))
            if (!SuppressWarnings)
                Console.WriteLine("WARNING: Already at a root directory. No work was performed.");
        else
            PushDirectoryAndGo(backOne);
    }


    // Preferred to a field implementation.

    /// <summary>
    /// Suppress warnings from this class.
    /// </summary>
    public static void SuppressDirectoryTaxiWarnings() => SuppressWarnings = true;

    /// <summary>
    /// Emit warnings from this class.
    /// </summary>
    public static void EmitDirectoryTaxiWarnings() => SuppressWarnings = false;
}
