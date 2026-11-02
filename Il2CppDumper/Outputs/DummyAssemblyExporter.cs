using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Il2CppDumper
{
    public static class DummyAssemblyExporter
    {
        public static void Export(Il2CppExecutor il2CppExecutor, string outputDir)
        {
            Directory.SetCurrentDirectory(outputDir);
            if (Directory.Exists("DummyDll"))
                Directory.Delete("DummyDll", true);
            Directory.CreateDirectory("DummyDll");
            Directory.SetCurrentDirectory("DummyDll");
            var dummy = new DummyAssemblyGenerator(il2CppExecutor);
            var failed = 0;
            foreach (var assembly in dummy.Assemblies)
            {
                using (var stream = new MemoryStream())
                {
                    try
                    {
                        assembly.Write(stream);
                    }
                    catch (Exception e)
                    {
                        // CTest 1.4.50: scrambled metadata can leave a few unserializable signatures;
                        // bisect out the offending top-level types instead of losing the whole assembly.
                        Console.WriteLine($"[CTest] write fail {assembly.MainModule.Name}: {e.GetType().Name} {e.Message.Split('\n')[0]}");
                        foreach (var frame in e.StackTrace.Split('\n').Take(6))
                        {
                            Console.WriteLine(frame);
                        }
                        stream.SetLength(0);
                        try
                        {
                            SanitizeAndWrite(assembly, stream);
                        }
                        catch (Exception e2)
                        {
                            failed++;
                            Console.WriteLine($"[CTest] write skip {assembly.MainModule.Name}: {e2.GetType().Name}");
                            continue;
                        }
                    }
                    File.WriteAllBytes(assembly.MainModule.Name, stream.ToArray());
                }
            }
            Console.WriteLine($"[CTest] dummy-dll write: {dummy.Assemblies.Count} total, {failed} skipped");
        }

        // Repeatedly writes the assembly; on failure, bisects for one offending top-level type
        // (toggling module membership and restoring everything but the culprit), reports it, drops
        // it permanently and retries, until the write succeeds or no types remain.
        private static void SanitizeAndWrite(Mono.Cecil.AssemblyDefinition assembly, MemoryStream stream)
        {
            var module = assembly.MainModule;
            for (var attempt = 0; attempt < 32; attempt++)
            {
                stream.SetLength(0);
                try
                {
                    assembly.Write(stream);
                    return;
                }
                catch (Exception)
                {
                }
                var candidates = module.Types.ToList();
                if (candidates.Count == 0)
                {
                    throw new NotSupportedException("no types left");
                }
                var bad = FindBadType(assembly, candidates);
                Console.WriteLine($"[CTest] sanitize: dropping type {bad.FullName} from {module.Name}");
                module.Types.Remove(bad);
            }
            throw new NotSupportedException("sanitize budget exhausted");
        }

        // Bisection over `candidates`; assumes the assembly currently fails to write and exactly one
        // candidate subtree is at fault. Toggled types are restored before returning.
        private static Mono.Cecil.TypeDefinition FindBadType(Mono.Cecil.AssemblyDefinition assembly, List<Mono.Cecil.TypeDefinition> candidates)
        {
            var module = assembly.MainModule;
            var toggled = new List<Mono.Cecil.TypeDefinition>();
            var span = candidates;
            while (span.Count > 1)
            {
                var half = (span.Count + 1) / 2;
                var latter = span.Skip(half).ToList();
                foreach (var t in latter)
                {
                    module.Types.Remove(t);
                    toggled.Add(t);
                }
                bool clean;
                using (var probe = new MemoryStream())
                {
                    try
                    {
                        assembly.Write(probe);
                        clean = true;
                    }
                    catch (Exception)
                    {
                        clean = false;
                    }
                }
                if (clean)
                {
                    // culprit is inside `latter` (it stays out); narrow there
                    span = latter;
                }
                else
                {
                    // culprit is inside the first half; put `latter` back
                    foreach (var t in latter)
                    {
                        module.Types.Add(t);
                        toggled.Remove(t);
                    }
                    span = span.Take(half).ToList();
                }
            }
            // restore everything except the single culprit
            foreach (var t in toggled)
            {
                module.Types.Add(t);
            }
            return span[0];
        }
    }
}
