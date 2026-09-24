using System.Collections;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using Xunit;

namespace RevitCortex.Tests.Core
{
    public class SafeScriptResultProjectorTests
    {
        [Fact]
        public void PlainDataAndMetadataKeepTheResponseContract()
        {
            var result = SafeScriptResultProjector.Project(new { count = 2, points = new[] { new { X = 1.5, Y = 2.0 } }, name = "Труба" }, "script.cs", "REUSABLE");
            Assert.Equal(2, result["result"]!["count"]);
            Assert.Equal(1.5, result["result"]!["points"]![0]!["X"]);
            Assert.Equal("Труба", result["result"]!["name"]);
            Assert.Equal("script.cs", result["scriptSavedTo"]);
            Assert.Equal("REUSABLE", result["scriptLifetime"]);
        }

        [Fact]
        public void NullAndScalarsAreSupported()
        {
            Assert.Equal(JTokenType.Null, SafeScriptResultProjector.Project(null)["result"]!.Type);
            Assert.Equal(long.MaxValue, SafeScriptResultProjector.Project(long.MaxValue)["result"]!.Value<long>());
            Assert.Equal("Friday", SafeScriptResultProjector.Project(DayOfWeek.Friday)["result"]!.Value<string>());
            Assert.Equal("true", SafeScriptResultProjector.Project(true)["result"]!.ToString(Formatting.None));
        }

        [Fact]
        public void LargeNormalExportFitsTheBudget()
        {
            var rows = Enumerable.Range(0, 3000).Select(i => new { id = i, name = "Element", x = 1, y = 2, z = 3 });
            Assert.Equal(3000, ((JArray)SafeScriptResultProjector.Project(rows)["result"]!).Count);
        }

        [Fact]
        public void LazyEnumerationRunsOnceOnCallingThreadAndDisposes()
        {
            var sequence = new TrackedSequence(3);
            var thread = Environment.CurrentManagedThreadId;
            Assert.Equal(3, ((JArray)SafeScriptResultProjector.Project(sequence)["result"]!).Count);
            Assert.Equal(1, sequence.Starts);
            Assert.True(sequence.Disposed);
            Assert.All(sequence.Threads, id => Assert.Equal(thread, id));
        }

        [Fact]
        public void RejectsRevitObjectsBeforeAnyGetterOrEnumerator()
        {
            var dangerous = new Autodesk.Revit.DB.UnsafeResultFixture();
            var ex = Assert.Throws<ScriptResultException>(() => SafeScriptResultProjector.Project(new { Bounds = dangerous }));
            Assert.Equal("RevitApiObject", ex.Reason);
            Assert.Equal("result.Bounds", ex.ResultPath);
            Assert.Equal(0, dangerous.Calls);
        }

        [Fact]
        public void RejectsRevitDerivedTypesOutsideAutodeskNamespace()
        {
            Assert.Equal("RevitApiObject", Assert.Throws<ScriptResultException>(() => SafeScriptResultProjector.Project(new DerivedRevitFixture())).Reason);
        }

        [Fact]
        public void RejectsCustomObjectsWithoutGettersOrToStringOrConverters()
        {
            var value = new GetterTrap();
            Assert.Equal("UnsupportedObject", Assert.Throws<ScriptResultException>(() => SafeScriptResultProjector.Project(value)).Reason);
            Assert.Equal(0, value.Calls);
        }

        [Fact]
        public void NestedRevitValueInLazySequenceFailsAndDisposes()
        {
            var sequence = new TrackedSequence(2, new Autodesk.Revit.DB.UnsafeResultFixture());
            Assert.Equal("result.0", Assert.Throws<ScriptResultException>(() => SafeScriptResultProjector.Project(sequence)).ResultPath);
            Assert.True(sequence.Disposed);
        }

        [Fact]
        public void CyclesFailButSharedNonCyclicReferencesWork()
        {
            var cycle = new ArrayList(); cycle.Add(cycle);
            Assert.Equal("ReferenceCycle", Assert.Throws<ScriptResultException>(() => SafeScriptResultProjector.Project(cycle)).Reason);
            var shared = new[] { 1, 2 };
            Assert.Equal(2, ((JArray)SafeScriptResultProjector.Project(new[] { shared, shared })["result"]!).Count);
        }

        [Fact]
        public void DepthLimitStopsDeepGraphs()
        {
            object value = 1;
            for (int i = 1; i < SafeScriptResultProjector.MaxDepth; i++) value = new[] { value };
            SafeScriptResultProjector.Project(value);
            Assert.Equal("DepthLimit", Assert.Throws<ScriptResultException>(() => SafeScriptResultProjector.Project(new[] { value })).Reason);
        }

        [Fact]
        public void EndlessSequenceStopsAtNodeBudgetAndDisposes()
        {
            var sequence = new TrackedSequence(int.MaxValue);
            Assert.Equal("NodeLimit", Assert.Throws<ScriptResultException>(() => SafeScriptResultProjector.Project(sequence)).Reason);
            Assert.True(sequence.Disposed);
            Assert.True(sequence.Threads.Count <= SafeScriptResultProjector.MaxNodes);
        }

        [Fact]
        public void StringBudgetCountsUtf8Bytes()
        {
            SafeScriptResultProjector.Project(new string('a', SafeScriptResultProjector.MaxStringBytes));
            Assert.Equal("StringLimit", Assert.Throws<ScriptResultException>(() => SafeScriptResultProjector.Project(new string('я', 150000))).Reason);
        }

        [Fact]
        public void ResponseBudgetCountsEscapedOutputAndMetadata()
        {
            var text = new string('a', 250000);
            Assert.Equal("ResponseSizeLimit", Assert.Throws<ScriptResultException>(() => SafeScriptResultProjector.Project(Enumerable.Repeat(text, 5))).Reason);
            var four = Enumerable.Repeat(text, 4).ToArray();
            SafeScriptResultProjector.Project(four);
            Assert.Equal("ResponseSizeLimit", Assert.Throws<ScriptResultException>(() => SafeScriptResultProjector.Project(four, new string('p', 100000))).Reason);
            Assert.Equal("ResponseSizeLimit", Assert.Throws<ScriptResultException>(() => SafeScriptResultProjector.Project(new[] { new string('я', 100000), new string('я', 100000) })).Reason);
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void RejectsNonFiniteNumbers(double value) => Assert.Equal("NonFiniteNumber", Assert.Throws<ScriptResultException>(() => SafeScriptResultProjector.Project(value)).Reason);

        [Fact]
        public void JsonTokensAreTraversedSafely()
        {
            Assert.Equal(2, SafeScriptResultProjector.Project(JObject.Parse("{\"a\":[1,2]}"))["result"]!["a"]![1]);
            Assert.Equal("RawJsonNotAllowed", Assert.Throws<ScriptResultException>(() => SafeScriptResultProjector.Project(new JRaw("{}"))).Reason);
            Assert.Equal("UnsupportedJsonToken", Assert.Throws<ScriptResultException>(() => SafeScriptResultProjector.Project(JValue.CreateComment("ignore"))).Reason);
        }

        [Fact]
        public void DictionariesRequireStringKeys()
        {
            Assert.Equal(3, SafeScriptResultProjector.Project(new Dictionary<string, object> { ["x"] = 3 })["result"]!["x"]);
            Assert.Equal("DictionaryKeyMustBeString", Assert.Throws<ScriptResultException>(() => SafeScriptResultProjector.Project(new Hashtable { [1] = 3 })).Reason);
        }

        [Fact]
        public void IteratorExceptionsAreReportedAndNotSilentlyTruncated()
        {
            var sequence = new TrackedSequence(4, throwAfterFirst: true);
            Assert.Equal("ResultEvaluationFailed", Assert.Throws<ScriptResultException>(() => SafeScriptResultProjector.Project(sequence)).Reason);
            Assert.True(sequence.Disposed);
        }

        [Theory]
        [InlineData("rolled_back", true)]
        [InlineData("not_managed", false)]
        [InlineData("unknown", false)]
        public void FailureTellsAgentWhetherRollbackIsConfirmed(string state, bool confirmed)
        {
            var failure = new ScriptResultException("RevitApiObject", "result.Bounds", typeof(GetterTrap)).ToFailure(state);
            Assert.False(failure.Success);
            Assert.Equal(CortexErrorCode.ResultSerializationFailed, failure.Error!.Code);
            Assert.Equal(state, failure.Error.Context!["transactionState"]);
            Assert.Equal("result.Bounds", failure.Error.Context["resultPath"]);
            Assert.Contains(confirmed ? "were rolled back" : "rollback is not confirmed", failure.Error.Message);
            Assert.Contains("do not retry automatically", failure.Error.Suggestion);
        }

        private sealed class DerivedRevitFixture : Autodesk.Revit.DB.UnsafeResultFixture { }
        private sealed class GetterTrap
        {
            public int Calls;
            public object Child { get { Calls++; throw new Exception("Getter must not run"); } }
            public override string ToString() { Calls++; throw new Exception("ToString must not run"); }
        }
        private sealed class TrackedSequence : IEnumerable
        {
            private readonly int _count;
            private readonly object? _value;
            private readonly bool _throw;
            public int Starts;
            public bool Disposed;
            public List<int> Threads = new();
            public TrackedSequence(int count, object? value = null, bool throwAfterFirst = false) { _count = count; _value = value; _throw = throwAfterFirst; }
            public IEnumerator GetEnumerator()
            {
                Starts++;
                try
                {
                    for (int i = 0; i < _count; i++)
                    {
                        Threads.Add(Environment.CurrentManagedThreadId);
                        if (_throw && i > 0) throw new InvalidOperationException();
                        yield return _value ?? i;
                    }
                }
                finally { Disposed = true; }
            }
        }
    }
}

// The namespace is intentional: exercises rejection without loading the native Revit runtime.
namespace Autodesk.Revit.DB
{
    public class UnsafeResultFixture : IEnumerable
    {
        public int Calls;
        public object Inverse { get { Calls++; throw new Exception("Must not read Revit properties"); } }
        public IEnumerator GetEnumerator() { Calls++; throw new Exception("Must not enumerate Revit objects"); }
        public override string ToString() { Calls++; throw new Exception("Must not format Revit objects"); }
    }
}
