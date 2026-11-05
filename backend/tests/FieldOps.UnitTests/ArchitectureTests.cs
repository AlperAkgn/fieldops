using System.Xml.Linq;

namespace FieldOps.UnitTests;

public sealed class ArchitectureTests
{
    [Theory]
    [InlineData("FieldOps.Domain", "")]
    [InlineData("FieldOps.Application", "FieldOps.Domain")]
    [InlineData("FieldOps.Infrastructure", "FieldOps.Application")]
    [InlineData("FieldOps.Api", "FieldOps.Application,FieldOps.Infrastructure")]
    public void ProjectReferencesPointInward(string project, string allowedReferences)
    {
        var projectFile = XDocument.Load(Path.Combine(FindBackendDirectory(), "src", project, $"{project}.csproj"));
        var references = projectFile.Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value))
            .Order().ToArray();
        var expected = allowedReferences.Split(',', StringSplitOptions.RemoveEmptyEntries).Order().ToArray();

        Assert.Equal(expected, references);
        if (project is "FieldOps.Domain" or "FieldOps.Application")
        {
            Assert.Empty(projectFile.Descendants("PackageReference"));
            Assert.Empty(projectFile.Descendants("FrameworkReference"));
        }
    }

    private static string FindBackendDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FieldOps.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Backend solution was not found.");
    }
}
