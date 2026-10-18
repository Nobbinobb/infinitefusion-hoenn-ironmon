using Microsoft.Data.Sqlite;

namespace Ironmon.Sqlite.Runtime;

/// <summary>
/// Exclusively owns one generated command until its reader has been disposed.
/// </summary>
/// <remarks>
/// Disposal clears managed inputs and the transaction. Commands with more than 4 KiB of combined
/// binary and conservative UTF-8 text inputs are disposed to release native bound copies as well.
/// Each connection retains at most 192 prepared commands with smaller inputs, one per query identity.
/// </remarks>
public sealed class SqliteCommandLease : IDisposable
{
    private const long _maximumRetainedInputBytes = 4096;
    private const int _maximumUtf8BytesPerCharacter = 3;
    private readonly SqliteCommandPool _owner;
    private readonly object _identity;
    private readonly long _generation;
    private readonly bool _retained;
    private bool _failed;
    private bool _disposed;

    /// <summary>
    /// Creates an exclusive lease for a command removed from its connection's idle pool.
    /// </summary>
    /// <param name="owner">The ephemeron-owned pool that created or retained the command.</param>
    /// <param name="identity">The generated query identity token.</param>
    /// <param name="generation">The current physical connection lifecycle generation.</param>
    /// <param name="command">The exclusively leased command and initialized parameter layout.</param>
    /// <param name="retained">Whether the pool reserved a reusable statement slot.</param>
    internal SqliteCommandLease(SqliteCommandPool owner, object identity, long generation, SqliteCommand command, bool retained)
    {
        _owner = owner;
        _identity = identity;
        _generation = generation;
        Command = command;
        _retained = retained;
    }

    /// <summary>
    /// Gets the exclusively leased command for generated transaction and ordinal value binding.
    /// </summary>
    /// <remarks>
    /// The generated caller owns reader disposal before lease disposal. It must not change command
    /// text, parameter layout or connection ownership, and must not dispose the command itself.
    /// </remarks>
    public SqliteCommand Command { get; }

    /// <summary>
    /// Executes the leased statement while discarding commands that fail native execution.
    /// </summary>
    /// <returns>The provider's original affected-row count.</returns>
    public int ExecuteNonQuery()
    {
        try
        {
            return Command.ExecuteNonQuery();
        }
        catch
        {
            _failed = true;
            throw;
        }
    }

    /// <summary>
    /// Executes the leased scalar statement without coercing native SQLite storage classes.
    /// </summary>
    /// <returns>The original first value, including DBNull or null when no row exists.</returns>
    public object? ExecuteScalar()
    {
        try
        {
            return Command.ExecuteScalar();
        }
        catch
        {
            _failed = true;
            throw;
        }
    }

    /// <summary>
    /// Opens an exclusively owned reader and discards commands that fail before reader creation.
    /// </summary>
    /// <returns>The provider reader, which must be disposed before this lease.</returns>
    public SqliteDataReader ExecuteReader()
    {
        try
        {
            return Command.ExecuteReader();
        }
        catch
        {
            _failed = true;
            throw;
        }
    }

    /// <summary>
    /// Advances the leased reader while retaining native failures for conservative command disposal.
    /// </summary>
    /// <param name="reader">The exclusively owned reader opened by this lease.</param>
    /// <returns>Whether the provider advanced to another row.</returns>
    public bool Read(SqliteDataReader reader)
    {
        try
        {
            return reader.Read();
        }
        catch
        {
            _failed = true;
            throw;
        }
    }

    /// <summary>
    /// Releases managed values and transaction references before returning or disposing the statement.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        long retainedBytes = 0;
        foreach (SqliteParameter parameter in Command.Parameters)
        {
            retainedBytes += parameter.Value switch
            {
                byte[] bytes => bytes.LongLength,
                string text => (long)text.Length * _maximumUtf8BytesPerCharacter,
                _ => 0
            };
            parameter.Value = DBNull.Value;
        }

        Command.Transaction = null;
        _owner.Return(_identity, _generation, Command, _retained, !_failed && retainedBytes <= _maximumRetainedInputBytes);
    }
}
