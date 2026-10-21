using RevitCortex.Core.Results;
using Newtonsoft.Json.Linq;
using Xunit;

namespace RevitCortex.Tests.Core
{
    public class ScriptElementIdProjectionTests
    {
        private static long? ConvertId(object value) => value is Autodesk.Revit.DB.IdValueFixture id ? id.Value : null;

        [Fact]
        public void AdapterConvertsIdsThroughoutNestedResultsWithoutTraversingProperties()
        {
            var id = new Autodesk.Revit.DB.IdValueFixture(3000000000L);
            var invalid = new Autodesk.Revit.DB.IdValueFixture(-1);
            var result = SafeScriptResultProjector.Project(new
            {
                Id = id, ids = new[] { id, invalid }.Select(x => x),
                map = new Dictionary<string, object> { ["id"] = id }
            }, elementIdValue: ConvertId)["result"]!;
            Assert.Equal(3000000000L, result["Id"]!.Value<long>());
            Assert.Equal(-1, result["ids"]![1]!.Value<long>());
            Assert.Equal(3000000000L, result["map"]!["id"]!.Value<long>());
            Assert.Equal(JTokenType.Integer, result["Id"]!.Type);
        }

        [Fact]
        public void AdapterDoesNotPermitOtherRevitObjects()
        {
            var value = new Autodesk.Revit.DB.UnsafeResultFixture();
            Assert.Equal("RevitApiObject", Assert.Throws<ScriptResultException>(() =>
                SafeScriptResultProjector.Project(value, elementIdValue: ConvertId)).Reason);
            Assert.Equal(0, value.Calls);
            Assert.Throws<ScriptResultException>(() => SafeScriptResultProjector.Project(new Autodesk.Revit.DB.IdValueFixture(1)));
        }
    }
}

namespace Autodesk.Revit.DB
{
    // The Core test seam stays free of native API calls. Tools' real ElementId adapter
    // is guarded separately; the real API conversion is also a Revit smoke-test item.
    public sealed class IdValueFixture(long value)
    {
        public long Value => value;
        public object Dangerous => throw new InvalidOperationException("Must not traverse properties");
    }
}
