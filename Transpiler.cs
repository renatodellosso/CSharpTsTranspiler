using BetterTasks;
using System.Reflection;

namespace TsTranspiler;

public class Transpiler
{

    public static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("No args passed. Usage: dotnet transpile-ts <dll-path> <output-dir>");
            return;
        }

        string dllPath = args[0], outputDir = args[1];

        FileInfo dllInfo = new FileInfo(dllPath);

        if (!File.Exists(dllInfo.FullName))
        {
            Console.WriteLine($"File not found: {dllPath}");
            return;
        }

        Directory.CreateDirectory(outputDir);

        Assembly assembly = Assembly.LoadFile(dllInfo.FullName);
        Console.WriteLine($"Loaded assembly: {assembly.GetName().Name}. Finding types to transpile...");
        Type[] types = assembly.GetTypes().Where(t => t.IsDefined(typeof(TranspileToTs))).ToArray();

        // Load third argument as number of threads
        int threads = args.Length > 2 ? int.Parse(args[2]) : (int)MathF.Ceiling(types.Length / 20f);

        Console.WriteLine($"Transpiling {types.Length} types using {threads} threads...");

        // Split types into an array for each tasks
        int chunkSize = types.Length / threads;
        Type[][] typeChunks = types.Chunk(chunkSize).ToArray();
        Console.WriteLine($"Type chunks: {typeChunks.Length}, Max Types in Chunk: {chunkSize}");

        // Start tasks
        BetterTask[] tasks = new BetterTask[threads];
        for (int i = 0; i < threads; i++)
        {
            Type[] typeChunk = typeChunks[i];
            BetterTask task = new(() => TranspileTask(typeChunk, outputDir));
            tasks[i] = task;
            task.Start();
        }

        // Wait for all tasks to complete
        Console.WriteLine("Waiting for all tasks to complete...");
        BetterTask.WaitAll(tasks);
        Console.WriteLine("All tasks completed.");

        Environment.Exit(0);
    }

    private static void TranspileTask(Type[] types, string outputDir)
    {
        Console.WriteLine($"Started thread with {types.Length} types...");

        foreach (Type type in types)
        {
            TranspileType(type, outputDir);
        }
    }

    private static void TranspileType(Type type, string outputDir)
    {
        Console.WriteLine($"Transpiling type: {type.FullName}");

        // Convert namespace to directory path. Skip the first element to remove the assembly name
        string[] ns = type.Namespace?.Split(".").Skip(1).ToArray() ?? [];

        foreach (string dir in ns)
        {
            outputDir = Path.Combine(outputDir, dir);
        }

        Console.WriteLine($"Output directory: {outputDir}");

        // Create directory if it doesn't exist
        Directory.CreateDirectory(outputDir);

        // Write to file
        string ts = ConvertTypeToTs(type);

        Console.WriteLine($"Writing to file: {type.Name}.ts");

        File.WriteAllText(Path.Combine(outputDir, type.Name + ".ts"), ts);
    }

    private static string ConvertTypeToTs(Type type)
    {
        string ts = "interface " + type.Name + " {\n";

        // Add fields
        FieldInfo[] fields = type.GetFields();
        foreach (FieldInfo field in fields)
        {
            ts += $"\t{field.Name}: {GetEquivalentType(field.FieldType)};\n";
        }

        ts += "}\n\nexport default " + type.Name + ";";

        return ts;
    }

    private static string GetEquivalentType(Type type)
    {
        return type.Name switch
        {
            "Single" or "Double" or "Int16" or "Int32" or "Int64" or "UInt16" or "UInt32" or "UInt64" or "Byte" => "number",
            "String" or "Char" => "string",
            "Boolean" => "boolean",
            _ => type.Name,
        };
    }

}