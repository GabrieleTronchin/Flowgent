using System.Reflection;
using ModelContextProtocol.Client;

namespace Spike.MafDeclarative;

/// <summary>
/// GATE-0 (FEAT-001) check 4: does the MCP CLIENT library leak ASP.NET Core?
///
/// A later phase wants to reference ONLY the client package (<c>ModelContextProtocol</c>)
/// from a plain classlib (a domain MCP server / parity test), WITHOUT dragging in
/// <c>Microsoft.AspNetCore.*</c>. This check reflects over the assembly that defines the
/// client type <see cref="McpClient"/> and asserts that none of its directly-referenced
/// assemblies starts with <c>Microsoft.AspNetCore</c>.
///
/// We deliberately anchor on a CLIENT type (<see cref="McpClient"/>) so we inspect the
/// client assembly, even though this spike (an Exe) also references the AspNetCore server
/// package for the HTTP server. <see cref="Assembly.GetReferencedAssemblies"/> reports what
/// the client assembly itself depends on, independent of what the Exe references.
/// </summary>
public static class AspNetCoreLeakCheck
{
    public static bool Run()
    {
        var clientAssembly = typeof(McpClient).Assembly;
        var referenced = clientAssembly.GetReferencedAssemblies();

        var aspNetRefs = referenced
            .Where(a => a.Name is not null &&
                        a.Name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal))
            .Select(a => a.Name!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var leaked = aspNetRefs.Count > 0;

        Console.WriteLine();
        Console.WriteLine(new string('=', 78));
        Console.WriteLine("GATE-0 CHECK 4: ASP.NET Core leak into the MCP client assembly");
        Console.WriteLine(new string('=', 78));
        Console.WriteLine($"[leak-check] client assembly: {clientAssembly.GetName().Name} ({clientAssembly.GetName().Version})");
        Console.WriteLine($"[leak-check] total referenced assemblies: {referenced.Length}");
        if (leaked)
        {
            Console.WriteLine($"[leak-check] RESULT: LEAK — client references {aspNetRefs.Count} Microsoft.AspNetCore assembly(ies):");
            foreach (var name in aspNetRefs)
            {
                Console.WriteLine($"[leak-check]   - {name}");
            }
        }
        else
        {
            Console.WriteLine("[leak-check] RESULT: NO LEAK — the client assembly references no Microsoft.AspNetCore assembly.");
        }

        // The check PASSES (returns true) when there is NO leak.
        return !leaked;
    }
}
