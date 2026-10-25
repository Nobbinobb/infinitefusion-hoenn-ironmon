using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Converts bounded Git progress frames into numeric measurements without displaying remote text.
/// </summary>
internal sealed partial class GitTransferProgress
{
    private const string Receiving = "Receiving objects";
    private const string Kibibytes = "KiB";
    private const string Mebibytes = "MiB";
    private const string Gibibytes = "GiB";
    private const string Pattern = @"^(Receiving objects|Resolving deltas):\s+\d+% \((\d+)/(\d+)\)(?:, ([0-9.]+) (bytes|KiB|MiB|GiB))?";
    private readonly StringBuilder _frame = new();
    private bool _oversized;

    /// <summary>
    /// Accepts arbitrary pipe chunks, including carriage-return updates and split UTF-8 reader buffers.
    /// </summary>
    /// <param name="text">The decoded standard-error chunk.</param>
    internal void Append(ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            if (character is '\r' or '\n')
            {
                if (!_oversized)
                    Report(_frame.ToString());

                _frame.Clear();
                _oversized = false;
            }
            else if (_frame.Length < 4096)
            {
                _frame.Append(character);
            }
            else
            {
                _oversized = true;
            }
        }
    }

    /// <summary>
    /// Validates numeric fields and ignores diagnostics, remote messages and malformed measurements.
    /// </summary>
    /// <param name="frame">One size-bounded Git progress line.</param>
    private static void Report(string frame)
    {
        var match = ProgressRegex().Match(frame);
        if (!match.Success || !long.TryParse(match.Groups[2].Value, out var completed) || !long.TryParse(match.Groups[3].Value, out var total) || total <= 0 || completed > total)
            return;

        var bytes = 0d;
        if (double.TryParse(match.Groups[4].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var size))
            bytes = size * (match.Groups[5].Value switch { Kibibytes => 1024d, Mebibytes => 1048576d, Gibibytes => 1073741824d, _ => 1d });

        if (!double.IsFinite(bytes) || bytes >= long.MaxValue)
            return;

        InstallationProgressScope.Report(new(match.Groups[1].Value == Receiving ? InstallationStage.DownloadingGame : InstallationStage.ProcessingGame, completed, total, (long)bytes));
    }

    /// <summary>
    /// Gets the invariant Git progress grammar selected by the private process environment.
    /// </summary>
    /// <returns>The generated bounded-frame parser.</returns>
    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex ProgressRegex();
}
