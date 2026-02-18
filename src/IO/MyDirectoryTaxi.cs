global using static MyMeteor.IO.MyDirectoryTaxi;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace MyMeteor.IO;

public class MyDirectoryTaxi
{
    private static readonly ConcurrentDictionary<int, Stack<string>> TaskStacks = [];
    private static Stack<string> _stack
    {
        get
        {
            if (!TaskStacks.ContainsKey(Task.CurrentId ?? -1)) TaskStacks[Task.CurrentId ?? -1] = [];
            return TaskStacks[Task.CurrentId ?? -1];
        }
    }

    private static bool SuppressWarnings = false;

    public static void PushDirectory() => _stack.Push(Environment.CurrentDirectory);
    public static void PushDirectory(string path) => _stack.Push(path);

    public static void PopDirectory()
    {
        if (_stack.Count > 0)
            _stack.Pop();
    }

    internal static void InternalPushDirectoryAndGo(string path)
    {
        _stack.Push(Environment.CurrentDirectory);
        Directory.SetCurrentDirectory(path);
    }

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
            _stack.Push(Environment.CurrentDirectory);
            if (!SuppressWarnings)
                Console.WriteLine($"WARNING: {path} provided to CreateDirectoryPushAndGo. The current directory was pushed onto the stack.");
        }
    }

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
            _stack.Push(Environment.CurrentDirectory);
            if (!SuppressWarnings)
                Console.WriteLine($"WARNING: {path} provided to PushDirectoryAndGo. The current directory was pushed onto the stack.");
        }
    }

    public static void PopDirectoryAndGo()
    {
        if (_stack.Count > 0)
            Directory.SetCurrentDirectory(_stack.Pop());
    }

    public static void PushDirectoryAndGoUp()
    {
        string backOne = Path.GetDirectoryName(Environment.CurrentDirectory);

        if (string.IsNullOrEmpty(backOne))
            if (!SuppressWarnings)
                Console.WriteLine("WARNING: Already at a root directory. No work was performed.");
        else
            PushDirectoryAndGo(backOne);
    }

    // Preferred to a field implementation
    public static void SuppressDirectoryTaxiWarnings() => SuppressWarnings = true;
    public static void EmitDirectoryTaxiWarnings() => SuppressWarnings = false;
}
