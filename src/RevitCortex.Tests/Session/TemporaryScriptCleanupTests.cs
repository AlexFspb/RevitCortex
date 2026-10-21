using RevitCortex.Core.Hosting;
using Xunit;
namespace RevitCortex.Tests.Session;
public class TemporaryScriptCleanupTests
{
    [Fact]
    public void ClosesReaderBeforeDeletingAndPreservesReusableFiles()
    {
        var folder = Path.Combine(Path.GetTempPath(), "cortex-cleanup-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        var temp = Path.Combine(folder, "temp.cs"); var reusable = Path.Combine(folder, "keep.cs");
        try
        {
            File.WriteAllText(temp, "// TEMP\nreturn null;");
            File.WriteAllText(reusable, "// REUSABLE\nreturn null;");
            TemporaryScriptCleanup.Clean(folder);
            Assert.False(File.Exists(temp)); Assert.True(File.Exists(reusable));
            File.Delete(reusable);
            TemporaryScriptCleanup.Clean(folder); Assert.False(Directory.Exists(folder));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
