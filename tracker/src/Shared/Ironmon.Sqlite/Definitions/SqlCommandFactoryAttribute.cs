namespace Ironmon.Sqlite.Querying;

/// <summary>
/// Adds a caller-owned reusable command factory to a generated non-query operation.
/// </summary>
/// <remarks>
/// The factory uses the same build-time query plan and exact parameter ordering as the execution method.
/// Callers dispose the command and can replace parameter values before repeated execution.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SqlCommandFactoryAttribute : Attribute;
