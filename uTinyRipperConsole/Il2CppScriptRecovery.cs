using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace uTinyRipperConsole
{
    internal static class Il2CppScriptRecovery
    {
        private sealed class Options
        {
            public string GamePath;
            public string Cpp2IlPath = "Cpp2IL";
            public string IlSpyPath = "ilspycmd";
            public string OutputPath;
        }

        private sealed class RecoveryReport
        {
            public string GamePath { get; set; }
            public string OutputPath { get; set; }
            public string Backend { get; set; }
            public string MetadataPath { get; set; }
            public string NativeBinaryPath { get; set; }
            public int AssembliesFound { get; set; }
            public int AssembliesDecompiled { get; set; }
            public List<string> DecompiledAssemblies { get; set; } = new List<string>();
            public List<string> FailedAssemblies { get; set; } = new List<string>();
            public string Status { get; set; }
        }

        public static int Run(string[] args)
        {
            Options options;
            try
            {
                options = ParseArguments(args);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                PrintUsage();
                return 2;
            }

            if (!Directory.Exists(options.GamePath))
            {
                Console.Error.WriteLine("Game directory does not exist: " + options.GamePath);
                return 2;
            }

            string fullGamePath = Path.GetFullPath(options.GamePath);
            string metadataPath;
            string nativeBinaryPath;
            if (!TryDetectIl2Cpp(fullGamePath, out metadataPath, out nativeBinaryPath))
            {
                Console.Error.WriteLine("No IL2CPP build was detected.");
                Console.Error.WriteLine("Expected global-metadata.dat and GameAssembly.dll, libil2cpp.so or GameAssembly.dylib.");
                return 3;
            }

            Directory.CreateDirectory(options.OutputPath);
            string cpp2IlOutput = Path.Combine(options.OutputPath, "Cpp2IL");
            string scriptsOutput = Path.Combine(options.OutputPath, "Scripts");
            Directory.CreateDirectory(cpp2IlOutput);
            Directory.CreateDirectory(scriptsOutput);

            RecoveryReport report = new RecoveryReport
            {
                GamePath = fullGamePath,
                OutputPath = Path.GetFullPath(options.OutputPath),
                Backend = "IL2CPP",
                MetadataPath = metadataPath,
                NativeBinaryPath = nativeBinaryPath,
                Status = "Running"
            };

            Console.WriteLine("[JDecompiler] IL2CPP build detected.");
            Console.WriteLine("[JDecompiler] Metadata: " + metadataPath);
            Console.WriteLine("[JDecompiler] Native binary: " + nativeBinaryPath);
            Console.WriteLine("[JDecompiler] Running Cpp2IL...");

            int cpp2IlExitCode = RunTool(
                options.Cpp2IlPath,
                new[]
                {
                    "--game-path=" + fullGamePath,
                    "--output-as=dll_il_recovery",
                    "--output-to=" + cpp2IlOutput
                });

            if (cpp2IlExitCode != 0)
            {
                report.Status = "Cpp2ILFailed";
                WriteReport(report, options.OutputPath);
                Console.Error.WriteLine("Cpp2IL exited with code " + cpp2IlExitCode + ".");
                return cpp2IlExitCode;
            }

            List<string> assemblies = FindRecoverableAssemblies(cpp2IlOutput);
            report.AssembliesFound = assemblies.Count;
            if (assemblies.Count == 0)
            {
                report.Status = "NoManagedAssembliesProduced";
                WriteReport(report, options.OutputPath);
                Console.Error.WriteLine("Cpp2IL completed, but no managed DLLs were found in its output.");
                return 4;
            }

            Console.WriteLine("[JDecompiler] Found " + assemblies.Count + " managed assemblies.");
            for (int i = 0; i < assemblies.Count; i++)
            {
                string assembly = assemblies[i];
                string assemblyName = Path.GetFileNameWithoutExtension(assembly);
                string assemblyOutput = Path.Combine(scriptsOutput, MakeSafeDirectoryName(assemblyName));
                Directory.CreateDirectory(assemblyOutput);

                Console.WriteLine("[JDecompiler] Decompiling " + Path.GetFileName(assembly) + " (" + (i + 1) + "/" + assemblies.Count + ")");

                int ilSpyExitCode = RunTool(
                    options.IlSpyPath,
                    new[]
                    {
                        "-p",
                        "--nested-directories",
                        "--disable-updatecheck",
                        "-o",
                        assemblyOutput,
                        assembly
                    });

                if (ilSpyExitCode == 0 && Directory.EnumerateFiles(assemblyOutput, "*.cs", SearchOption.AllDirectories).Any())
                {
                    report.AssembliesDecompiled++;
                    report.DecompiledAssemblies.Add(Path.GetFileName(assembly));
                }
                else
                {
                    report.FailedAssemblies.Add(Path.GetFileName(assembly) + " (exit " + ilSpyExitCode + ")");
                }
            }

            report.Status = report.FailedAssemblies.Count == 0 ? "Completed" :
                report.AssembliesDecompiled > 0 ? "CompletedWithErrors" : "DecompilationFailed";
            WriteReport(report, options.OutputPath);

            Console.WriteLine();
            Console.WriteLine("[JDecompiler] Script recovery finished.");
            Console.WriteLine("Assemblies found: " + report.AssembliesFound);
            Console.WriteLine("Assemblies decompiled: " + report.AssembliesDecompiled);
            Console.WriteLine("Assemblies failed: " + report.FailedAssemblies.Count);
            Console.WriteLine("C# output: " + Path.GetFullPath(scriptsOutput));
            Console.WriteLine("Report: " + Path.Combine(Path.GetFullPath(options.OutputPath), "recovery-report.json"));

            return report.AssembliesDecompiled > 0 ? 0 : 5;
        }

        private static Options ParseArguments(string[] args)
        {
            if (args.Length < 2)
                throw new ArgumentException("Missing game directory.");

            Options options = new Options();
            options.GamePath = args[1];

            for (int i = 2; i < args.Length; i++)
            {
                string argument = args[i];
                if (i + 1 >= args.Length)
                    throw new ArgumentException("Missing value for option " + argument);

                string value = args[++i];
                if (argument == "--cpp2il")
                    options.Cpp2IlPath = value;
                else if (argument == "--ilspy")
                    options.IlSpyPath = value;
                else if (argument == "--output")
                    options.OutputPath = value;
                else
                    throw new ArgumentException("Unknown option: " + argument);
            }

            if (string.IsNullOrWhiteSpace(options.OutputPath))
            {
                string gameName = new DirectoryInfo(Path.GetFullPath(options.GamePath)).Name;
                options.OutputPath = Path.Combine("Ripped", gameName, "IL2CPP");
            }

            options.OutputPath = Path.GetFullPath(options.OutputPath);
            return options;
        }

        private static bool TryDetectIl2Cpp(string gamePath, out string metadataPath, out string nativeBinaryPath)
        {
            metadataPath = null;
            nativeBinaryPath = null;

            try
            {
                metadataPath = Directory.EnumerateFiles(gamePath, "global-metadata.dat", SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (metadataPath == null)
                    return false;

                string[] nativeNames = { "GameAssembly.dll", "libil2cpp.so", "GameAssembly.dylib" };
                foreach (string nativeName in nativeNames)
                {
                    nativeBinaryPath = Directory.EnumerateFiles(gamePath, nativeName, SearchOption.AllDirectories)
                        .FirstOrDefault();
                    if (nativeBinaryPath != null)
                        return true;
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine("Could not scan part of the game directory: " + ex.Message);
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine("Could not scan the game directory: " + ex.Message);
            }

            metadataPath = null;
            nativeBinaryPath = null;
            return false;
        }

        private static List<string> FindRecoverableAssemblies(string root)
        {
            string[] excludedPrefixes =
            {
                "UnityEngine", "Unity.", "System.", "Microsoft.", "Mono.",
                "mscorlib", "netstandard", "Newtonsoft.Json"
            };

            return Directory.EnumerateFiles(root, "*.dll", SearchOption.AllDirectories)
                .Where(path =>
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    return !excludedPrefixes.Any(prefix =>
                        name.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                })
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static int RunTool(string executable, IEnumerable<string> arguments)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            string fullExecutable = executable;
            bool runWithDotNet = executable.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

            if (runWithDotNet)
            {
                startInfo.FileName = "dotnet";
                startInfo.ArgumentList.Add(Path.GetFullPath(executable));
            }
            else
            {
                startInfo.FileName = fullExecutable;
            }

            foreach (string argument in arguments)
                startInfo.ArgumentList.Add(argument);

            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;

            try
            {
                using (Process process = new Process())
                {
                    process.StartInfo = startInfo;
                    process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs eventArgs)
                    {
                        if (!string.IsNullOrEmpty(eventArgs.Data))
                            Console.WriteLine(eventArgs.Data);
                    };
                    process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs eventArgs)
                    {
                        if (!string.IsNullOrEmpty(eventArgs.Data))
                            Console.Error.WriteLine(eventArgs.Data);
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();
                    return process.ExitCode;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Could not start '" + executable + "': " + ex.Message);
                return -1;
            }
        }

        private static string MakeSafeDirectoryName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');
            return value;
        }

        private static void WriteReport(RecoveryReport report, string outputPath)
        {
            string reportPath = Path.Combine(outputPath, "recovery-report.json");
            string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(reportPath, json, new UTF8Encoding(false));
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  uTinyRipperCmd --recover-il2cpp <game-directory> [--cpp2il <executable>] [--ilspy <executable>] [--output <directory>]");
            Console.WriteLine();
            Console.WriteLine("Defaults: Cpp2IL='Cpp2IL', ILSpy='ilspycmd'.");
        }
    }
}
