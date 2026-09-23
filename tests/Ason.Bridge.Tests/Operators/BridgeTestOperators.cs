using Ason;

namespace Ason.Bridge.Tests.Operators;

/// <summary>
/// Instance operator: callable through an ASON handle, exactly like a view operator in a real application.
/// The handle is passed to the constructor because <see cref="OperatorBase.Handle"/> is protected.
/// </summary>
[AsonOperator("Bridge test instance operator")]
public sealed class BridgeCalculatorOperator : OperatorBase {

    public BridgeCalculatorOperator(string handle = "BridgeCalculatorOperator") {
        Handle = handle;
    }

    [AsonMethod("Adds two integers")]
    public int Add(int left, int right) => left + right;

    [AsonMethod("Concatenates two strings")]
    public string Concat(string left, string right) => left + right;

    [AsonMethod("Echoes the supplied model")]
    public BridgeTestModel Echo(BridgeTestModel model) => model;
}

/// <summary>
/// Static operator module: has no handle at all and is addressed by its type name.
/// </summary>
[AsonOperator("Bridge test static module")]
public static class BridgeStaticOperator {

    [AsonMethod("Adds two integers without an instance")]
    public static int Add(int left, int right) => left + right;

    [AsonMethod("Repeats the supplied text")]
    public static string Repeat(string text, int times) => string.Concat(Enumerable.Repeat(text, times));

    [AsonMethod("Throws, so the bridge can be tested against a failing operator")]
    public static void AlwaysFails() => throw new InvalidOperationException("operator failed on purpose");
}

/// <summary>Records which thread an invocation ran on, so UI-thread affinity can be asserted.</summary>
[AsonOperator("Bridge test thread probe")]
public sealed class ThreadProbeOperator : OperatorBase {

    public ThreadProbeOperator(string handle = "ThreadProbeOperator") {
        Handle = handle;
    }

    public int LastThreadId { get; private set; }

    [AsonMethod("Records and returns the calling thread id")]
    public int ProbeThread() {
        LastThreadId = Environment.CurrentManagedThreadId;
        return LastThreadId;
    }
}

/// <summary>An <c>[AsonModel]</c> data transfer type, listed by the manifest.</summary>
[AsonModel("Bridge test model")]
public sealed class BridgeTestModel {
    public int A { get; set; }
    public string? Name { get; set; }
}
