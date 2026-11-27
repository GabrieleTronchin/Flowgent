namespace Flowgent.Samples.Tests;

/// <summary>Folders of the samples, found from the test output folder (the one holding <c>Flowgent.Samples.slnx</c>).</summary>
public static class SamplesPaths
{
    private const string SamplesSolution = "Flowgent.Samples.slnx";

    public static readonly string Root = FindRoot();

    public static string Sample(params string[] parts) => Path.Combine([Root, .. parts]);

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SamplesSolution)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"Samples folder (containing {SamplesSolution}) not found.");
    }
}
