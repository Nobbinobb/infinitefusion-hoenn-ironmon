using System.Data;
using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;

namespace Ironmon.Sqlite.Execution;

/// <summary>
/// Reuses bounded generated commands on their owning connection without sharing active readers.
/// </summary>
public static class SqliteCommandCache
{
    private static readonly ConditionalWeakTable<SqliteConnection, SqliteCommandPool> _connections = [];
    private static readonly Lock _initialization = new();

    /// <summary>
    /// Leases an idle command or creates an independent command for overlapping execution.
    /// </summary>
    /// <param name="connection">The externally owned connection whose prepared statements may be reused.</param>
    /// <param name="identity">The compiler-generated identity of this query and its exact parameter layout.</param>
    /// <param name="create">The generated factory that initializes command text and typed parameter slots.</param>
    /// <returns>A lease whose disposal releases values and returns or disposes the command.</returns>
    public static SqliteCommandLease Rent(SqliteConnection connection, object identity, Func<SqliteConnection, SqliteCommand> create)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(create);
        if (!_connections.TryGetValue(connection, out SqliteCommandPool? commands))
        {
            lock (_initialization)
                commands = _connections.GetValue(connection, static source => new SqliteCommandPool(source));
        }

        return commands.Rent(identity, create);
    }
}

/// <summary>
/// Owns one connection's idle statements while allowing each active enumerator an exclusive command.
/// </summary>
internal sealed class SqliteCommandPool
{
    private const int _capacity = 192;
    private readonly Lock _gate = new();
    private readonly SqliteConnection _connection;
    private readonly Dictionary<object, SqliteCommand> _idle = new(ReferenceEqualityComparer.Instance);
    private long _generation;
    private int _retained;

    /// <summary>
    /// Creates an ephemeron-owned pool and invalidates idle commands when its connection closes.
    /// </summary>
    /// <param name="connection">The externally owned physical provider connection.</param>
    internal SqliteCommandPool(SqliteConnection connection)
    {
        _connection = connection;
        connection.StateChange += OnStateChange;
    }

    /// <summary>
    /// Removes one idle command from circulation or reserves a bounded reusable command slot.
    /// </summary>
    /// <param name="identity">The generated query's exact identity token.</param>
    /// <param name="create">The generated command and parameter-layout initializer.</param>
    /// <returns>An exclusive lease for this execution or streaming enumeration.</returns>
    internal SqliteCommandLease Rent(object identity, Func<SqliteConnection, SqliteCommand> create)
    {
        lock (_gate)
        {
            if (_idle.Remove(identity, out SqliteCommand? command))
                return new(this, identity, _generation, command, true);

            bool retained = _retained < _capacity;
            command = create(_connection);
            if (retained)
                _retained++;

            return new(this, identity, _generation, command, retained);
        }
    }

    /// <summary>
    /// Retains at most one idle statement per query without carrying leases across close/reopen cycles.
    /// </summary>
    /// <param name="identity">The generated query identity.</param>
    /// <param name="generation">The connection generation in which this command was leased.</param>
    /// <param name="command">The completed command whose reader and values have been released.</param>
    /// <param name="retained">Whether this lease reserved a reusable bounded slot.</param>
    /// <param name="reusable">Whether execution and input-retention limits permit reuse.</param>
    internal void Return(object identity, long generation, SqliteCommand command, bool retained, bool reusable)
    {
        lock (_gate)
        {
            if (retained && reusable && generation == _generation && _connection.State == ConnectionState.Open && _idle.TryAdd(identity, command))
                return;

            if (retained && generation == _generation)
                _retained--;
        }

        command.Dispose();
    }

    /// <summary>
    /// Drops all idle statements after native connection closure and invalidates outstanding lease returns.
    /// </summary>
    /// <param name="sender">The externally owned connection emitting the lifecycle transition.</param>
    /// <param name="change">The provider's old and new connection states.</param>
    private void OnStateChange(object? sender, StateChangeEventArgs change)
    {
        if (change.CurrentState == ConnectionState.Open)
            return;

        SqliteCommand[] expired;
        lock (_gate)
        {
            _generation++;
            expired = [.. _idle.Values];
            _retained = 0;
            _idle.Clear();
        }

        foreach (SqliteCommand command in expired)
            command.Dispose();
    }
}
