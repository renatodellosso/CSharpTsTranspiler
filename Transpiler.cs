using BetterTasks;
using System.Reflection;

namespace TsTranspiler;

public class Transpiler
{

    private static string outputDir = "";

    /// <summary>
    /// Full names of all the types being transpiled
    /// </summary>
    private static string[] typeNames;

    public static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("No args passed. Usage: dotnet transpile-ts <dll-path> <output-dir>");
            return;
        }

        string dllPath = args[0];
        outputDir = args[1];

        FileInfo dllInfo = new(dllPath);

        if (!File.Exists(dllInfo.FullName))
        {
            Console.WriteLine($"File not found: {dllPath}");
            return;
        }

        Assembly assembly = Assembly.LoadFile(dllInfo.FullName);
        Console.WriteLine($"Loaded assembly: {assembly.GetName().Name}. Finding types to transpile...");
        Type[] types = assembly.GetTypes().Where(t => t.IsDefined(typeof(Transpile))).ToArray();

        // Read type names
        typeNames = types.Select(t => t.FullName).ToArray();

        int threads = (int)MathF.Ceiling(types.Length / 20f);

        // Load other arguments
        for (int i = 2; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == "-t" || arg == "--threads")
            {
                threads = int.Parse(args[i + 1]);
                i++;
            }
            else if (arg == "-d" || arg == "--delete-output-dir")
            {
                Console.WriteLine("Deleting output directory...");
                Directory.Delete(outputDir, true);
            }
        }

        // Set up folder
        Directory.CreateDirectory(outputDir);

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
            BetterTask task = new(() => TranspileTask(typeChunk));
            tasks[i] = task;
            task.Start();
        }

        // Wait for all tasks to complete
        Console.WriteLine("Waiting for all tasks to complete...");
        BetterTask.WaitAll(tasks);
        Console.WriteLine("All tasks completed.");

        Environment.Exit(0);
    }

    private static void TranspileTask(Type[] chunk)
    {
        Console.WriteLine($"Started thread with {chunk.Length} types...");

        foreach (Type type in chunk)
            TranspileType(type);
    }

    private static void TranspileType(Type type)
    {
        Console.WriteLine($"Transpiling type: {type.FullName}");

        // Convert namespace to directory path. Skip the first element to remove the assembly name
        string dir = NamespaceToOutputDir(type.Namespace);

        Console.WriteLine($"Output directory: {dir}");

        // Create directory if it doesn't exist
        Directory.CreateDirectory(dir);

        // Write to file
        string ts = ConvertTypeToTs(type);

        Console.WriteLine($"Writing to file: {type.Name}.ts");

        File.WriteAllText(Path.Combine(dir, type.Name + ".ts"), ts);
    }

    private static string ConvertTypeToTs(Type type)
    {
        FieldInfo[] fields = type.GetFields();

        List<string> imports = [];
        string[] variableDeclarations = new string[fields.Length];

        string[] typeNs = type.Namespace?.Split(".").Skip(1).ToArray() ?? [];

        // Add fields
        for (int i = 0; i < fields.Length; i++)
        {
            FieldInfo field = fields[i];
            Type fieldType = field.FieldType;
            string tsType = GetEquivalentType(fieldType);

            // Check if the type is an object and a type being transpiled
            if (tsType == fieldType.Name && typeNames.Contains(fieldType.FullName))
            {
                // Convert namespace to directory path. Skip the first element to remove the assembly name
                string[] ns = fieldType.Namespace?.Split(".").Skip(1).ToArray() ?? [];

                // Find the relative path to the type
                int commonLength = 0;
                for (int j = 0; j < Math.Min(ns.Length, typeNs.Length); j++)
                {
                    if (ns[j] == typeNs[j]) commonLength++;
                    else break;
                }

                string importPath = $"./";
                for (int j = commonLength; j < typeNs.Length; j++)
                    importPath += "../";

                importPath += string.Join("/", ns);

                if (ns.Any()) importPath += "/";
                importPath += fieldType.Name;

                imports.Add($"import {fieldType.Name} from '{importPath}';");
            }

            variableDeclarations[i] = $"{field.Name}: {tsType};";
        }

        // Generate file
        string ts = imports.Any() ? string.Join("\n", imports) + "\n\n" : "";
        ts += "interface " + type.Name + " {\n\t";
        ts += string.Join("\n\t", variableDeclarations);
        ts += "\n}\n\nexport default " + type.Name + ";";

        return ts;
    }

    private static string GetEquivalentType(Type type)
    {
        // Check if the type is a nullable type
        if (Nullable.GetUnderlyingType(type) == null)
        {
            return type.Name switch
            {
                "Single" or "Double" or "Int16" or "Int32" or "Int64"
                    or "UInt16" or "UInt32" or "UInt64" or "Byte" => "number",
                "String" or "Char" => "string",
                "Boolean" => "boolean",
                _ => type.Name,
            };
        }

        // Nullable type
        return GetEquivalentType(Nullable.GetUnderlyingType(type)) + " | null";
    }

    private static string NamespaceToOutputDir(string? ns)
    {
        string[] nsArr = ns?.Split(".").Skip(1).ToArray() ?? [];
        return Path.Combine(outputDir, Path.Combine(nsArr));
    }

}