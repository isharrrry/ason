using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Ason.Invocation;

public sealed record OperatorMethodEntry(MethodInfo Method, ParameterInfo[] Parameters, bool IsGenericDefinition, bool ReturnsTask, bool ReturnsTaskWithResult, Type? ResultType);

public interface IOperatorMethodCache {
    bool TryGet(Type declaringType, string name, int argCount, out OperatorMethodEntry entry);
    /// <summary>Resolves a method on a static operator module (a static class annotated with [AsonOperator]).</summary>
    bool TryGetStatic(string targetTypeName, string name, int argCount, out OperatorMethodEntry entry);
    // For generic methods create/lookup closed form
    OperatorMethodEntry GetOrAddClosedGeneric(OperatorMethodEntry openEntry, Type[] typeArguments);
}

internal sealed class OperatorInvoker : IOperatorInvoker {
    readonly ConcurrentDictionary<string, OperatorBase> _handleToObject;
    readonly ConcurrentDictionary<string, object>? _singletonOperators;
    readonly IInvocationScheduler _scheduler;
    readonly JsonSerializerOptions _jsonOptions;
    readonly IOperatorMethodCache _methodCache;

    public OperatorInvoker(ConcurrentDictionary<string, OperatorBase> handleToObject, IInvocationScheduler scheduler, JsonSerializerOptions jsonOptions, IOperatorMethodCache methodCache, ConcurrentDictionary<string, object>? singletonOperators = null) {
        _handleToObject = handleToObject; _scheduler = scheduler; _jsonOptions = jsonOptions; _methodCache = methodCache;
        _singletonOperators = singletonOperators;
    }

    public async Task<object?> InvokeAsync(string target, string method, string? handleId, object?[]? args) {
        var argCount = args?.Length ?? 0;

        // Operator modules (static classes annotated with [AsonOperator]) carry no handle: the generated
        // proxy passes the type name as the target, so dispatch straight to the static method.
        if (string.IsNullOrEmpty(handleId)) {
            if (!_methodCache.TryGetStatic(target, method, argCount, out var staticEntry)) {
                throw new ArgumentNullException(nameof(handleId),
                    $"Operator '{target}.{method}' was invoked without a handle. Declare the operator as a static class to make it callable without one, or expose it as an OperatorBase instance.");
            }

            var staticArgs = staticEntry.Method.CoerceMethodArguments(args ?? Array.Empty<object?>(), _jsonOptions);
            async Task<object?> InvokeStaticAsync() => await NormalizeResultAsync(staticEntry.Method.Invoke(null, staticArgs)).ConfigureAwait(false);
            return await _scheduler.InvokeAsync(InvokeStaticAsync).ConfigureAwait(false);
        }

        object instance;
        if (!_handleToObject.TryGetValue(handleId, out var operatorInstance) || operatorInstance is null) {
            _handleToObject.TryRemove(handleId, out _);
            // Marker-only operators ([AsonOperator] without OperatorBase) are registered as singletons.
            if (_singletonOperators is null || !_singletonOperators.TryGetValue(handleId, out var singleton) || singleton is null) {
                throw new ObjectDisposedException(handleId);
            }
            instance = singleton;
        }
        else {
            instance = operatorInstance;
        }

        var type = instance.GetType();
        if (!_methodCache.TryGet(type, method, argCount, out var entry)) {
            throw new MissingMethodException(type.FullName, method);
        }

        // Generic method inference placeholder (simple heuristic based on argument runtime types)
        if (entry.IsGenericDefinition) {
            var genArgs = entry.Method.GetGenericArguments();
            var inferred = new Type[genArgs.Length];
            for (int i = 0; i < genArgs.Length; i++) inferred[i] = typeof(object); // placeholder inference strategy
            entry = _methodCache.GetOrAddClosedGeneric(entry, inferred);
        }

        var coerced = entry.Method.CoerceMethodArguments(args ?? Array.Empty<object?>(), _jsonOptions);

        async Task<object?> InvokeCoreAsync() {
            if (instance is OperatorBase op) await op.Reload();
            var res = entry.Method.Invoke(instance, coerced);
            return await NormalizeResultAsync(res).ConfigureAwait(false);
        }

        return await _scheduler.InvokeAsync(InvokeCoreAsync);
    }

    static async Task<object?> NormalizeResultAsync(object? invocationResult) {
        if (invocationResult is Task task) {
            await task.ConfigureAwait(false);
            if (task.GetType().IsGenericType) {
                object? value = task.GetType().GetProperty("Result")!.GetValue(task);
                if (value is OperatorBase modelOperator) return modelOperator.Handle;
                return value;
            }
            return null;
        }
        // NEW: convert direct OperatorBase returns to handle so proxy pattern stays uniform (previously only Task<OperatorBase> was handled)
        if (invocationResult is OperatorBase opInstance) return opInstance.Handle;
        return invocationResult;
    }
}
