using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace VendingManager;

public sealed class ConsolidationStore(string databasePath)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();

    public async Task<int> SyncAsync(VendingConfig vending, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(vending.LogPath)) return 0;
        var lines = await ReadStableLinesAsync(vending.LogPath, cancellationToken);
        return await ImportLinesAsync(vending, lines, cancellationToken);
    }

    // Import every row first. Only after SQLite commits do we remove final rows from the selected
    // date. A byte-for-byte comparison prevents deleting data changed by the API during import.
    public async Task<PruneResult> ImportAndPruneDateAsync(
        VendingConfig vending, DateOnly date, string backupDirectory, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(vending.LogPath)) return new PruneResult(0, 0, 0);
        var original = await ReadStableBytesAsync(vending.LogPath, cancellationToken);
        var snapshotLines = DecodeLines(original);
        var imported = await ImportLinesAsync(vending, snapshotLines, cancellationToken);
        var path = Path.GetFullPath(vending.LogPath);

        await using var locked = await OpenExclusiveAsync(path, cancellationToken);
        var current = await ReadAllBytesAsync(locked, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(original), SHA256.HashData(current)))
            throw new IOException("El archivo .log cambió durante la importación. No se borró ninguna fila; vuelve a consultar cuando termine la sincronización.");

        var remaining = new List<string>();
        var removed = 0;
        var pending = 0;
        foreach (var line in DecodeLines(current))
        {
            if (!TryParse(line, out var record) || DateOnly.FromDateTime(record.At.DateTime) != date)
            {
                remaining.Add(line);
                continue;
            }
            if (record.Status == "PENDIENTE")
            {
                pending++;
                remaining.Add(line);
                continue;
            }

            // An older release could have concatenated the new transaction to a preceding row.
            // Preserve that prefix even when the transaction suffix is removed.
            var start = FindTransactionStart(line);
            if (start > 0) remaining.Add(line[..(start - 1)]);
            removed++;
        }

        if (removed == 0) return new PruneResult(imported, 0, pending);
        Directory.CreateDirectory(backupDirectory);
        var safeName = string.Concat(vending.Name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var backupPath = Path.Combine(backupDirectory, $"{safeName}_{date:yyyyMMdd}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.log.bak");
        await File.WriteAllBytesAsync(backupPath, current, cancellationToken);

        var newline = DetectNewline(current);
        var outputText = remaining.Count == 0 ? "" : string.Join(newline, remaining) + newline;
        var output = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(outputText);
        try
        {
            locked.Position = 0;
            locked.SetLength(0);
            await locked.WriteAsync(output, cancellationToken);
            await locked.FlushAsync(cancellationToken);
            locked.Flush(flushToDisk: true);
        }
        catch
        {
            locked.Position = 0;
            locked.SetLength(0);
            await locked.WriteAsync(current, CancellationToken.None);
            await locked.FlushAsync(CancellationToken.None);
            locked.Flush(flushToDisk: true);
            throw;
        }
        return new PruneResult(imported, removed, pending);
    }

    private async Task<int> ImportLinesAsync(VendingConfig vending, string[] lines, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using (var schema = connection.CreateCommand())
        {
            schema.CommandText = """
                CREATE TABLE IF NOT EXISTS Credits(
                  Vending TEXT NOT NULL, Id TEXT NOT NULL, At TEXT NOT NULL,
                  Bank TEXT NOT NULL, UserName TEXT NOT NULL, Amount TEXT NOT NULL,
                  Status TEXT NOT NULL, Detail TEXT NOT NULL,
                  PRIMARY KEY(Vending, Id));
                CREATE INDEX IF NOT EXISTS IX_Credits_At ON Credits(Vending, At);
                """;
            await schema.ExecuteNonQueryAsync(cancellationToken);
        }

        var count = 0;
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryParse(line, out var record)) continue;
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)tx;
            command.CommandText = """
                INSERT INTO Credits(Vending,Id,At,Bank,UserName,Amount,Status,Detail)
                VALUES($v,$id,$at,$bank,$user,$amount,$status,$detail)
                ON CONFLICT(Vending,Id) DO UPDATE SET
                  At=excluded.At, Bank=excluded.Bank, UserName=excluded.UserName,
                  Amount=excluded.Amount, Status=excluded.Status, Detail=excluded.Detail;
                """;
            command.Parameters.AddWithValue("$v", vending.Name);
            command.Parameters.AddWithValue("$id", record.Id);
            command.Parameters.AddWithValue("$at", record.At.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$bank", record.Bank);
            command.Parameters.AddWithValue("$user", record.User);
            command.Parameters.AddWithValue("$amount", record.Amount.ToString(CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$status", record.Status);
            command.Parameters.AddWithValue("$detail", record.Detail);
            await command.ExecuteNonQueryAsync(cancellationToken);
            count++;
        }
        await tx.CommitAsync(cancellationToken);
        return count;
    }

    private static string[] DecodeLines(byte[] bytes)
    {
        using var memory = new MemoryStream(bytes);
        using var reader = new StreamReader(memory, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd().Split(new[] { "\\r\\n", "\\n", "\\r" }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static async Task<byte[]> ReadStableBytesAsync(string path, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await File.ReadAllBytesAsync(path, ct);
            }
            catch (IOException) when (attempt < 4) { await Task.Delay(300, ct); }
        }
    }

    private static async Task<FileStream> OpenExclusiveAsync(string path, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough); }
            catch (IOException) when (attempt < 8) { await Task.Delay(250, ct); }
        }
    }

    private static async Task<byte[]> ReadAllBytesAsync(FileStream stream, CancellationToken ct)
    {
        stream.Position = 0;
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, ct);
        return memory.ToArray();
    }

    private static string DetectNewline(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        return text.Contains("\\r\\n", StringComparison.Ordinal) ? "\\r\\n" : "\\n";
    }

    private static int FindTransactionStart(string line)
    {
        var fields = line.Split('|');
        for (var i = 0; i + 7 < fields.Length; i++)
        {
            if (DateTimeOffset.TryParse(fields[i + 1], CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out _) &&
                decimal.TryParse(fields[i + 4], NumberStyles.Number, CultureInfo.InvariantCulture, out _) &&
                fields[i + 5].Trim().ToUpperInvariant() is "OK" or "ERROR" or "PENDIENTE")
                return line.IndexOf(fields[i], StringComparison.Ordinal);
        }
        return 0;
    }

    private static async Task<string[]> ReadStableLinesAsync(string path, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8, true);
                var text = await reader.ReadToEndAsync(ct);
                return text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
            }
            catch (IOException) when (attempt < 3) { await Task.Delay(350, ct); }
        }
    }

    public async Task<List<DailySummary>> QueryAsync(string vending, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        var all = await QueryRecordsAsync(vending, start, end, ct);
        return all.GroupBy(x => DateOnly.FromDateTime(x.At.DateTime))
            .OrderBy(x => x.Key)
            .Select(g => new DailySummary(g.Key,
                g.Sum(x => x.Amount),
                g.Where(x => x.Status == "OK").Sum(x => x.Amount),
                g.Where(x => x.Status == "ERROR").Sum(x => x.Amount),
                g.Where(x => x.Status == "PENDIENTE").Sum(x => x.Amount),
                g.Where(x => x.Status == "SIN ESTADO").Sum(x => x.Amount)))
            .ToList();
    }

    public async Task<List<CreditRecord>> QueryRecordsAsync(string vending, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        var result = new List<CreditRecord>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,At,Bank,UserName,Amount,Status,Detail FROM Credits WHERE Vending=$v ORDER BY At DESC;";
        command.Parameters.AddWithValue("$v", vending);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (!DateTimeOffset.TryParse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)) continue;
            if (DateOnly.FromDateTime(at.DateTime) < start || DateOnly.FromDateTime(at.DateTime) > end) continue;
            decimal.TryParse(reader.GetString(4), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount);
            result.Add(new CreditRecord(reader.GetString(0), at, reader.GetString(2), reader.GetString(3), amount,
                reader.GetString(5), reader.GetString(6)));
        }
        return result;
    }

    private static bool TryParse(string line, out CreditRecord record)
    {
        record = default!;
        var f = line.Split('|');
        // Current API format: ID | transaction time | bank | user | amount | status | update time | detail.
        // Also recovers old rows where the previous final line was concatenated before the ID.
        for (var i = 0; i + 7 < f.Length; i++)
        {
            if (!DateTimeOffset.TryParse(f[i + 1], CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var at)) continue;
            if (!decimal.TryParse(f[i + 4], NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)) continue;
            var status = f[i + 5].Trim().ToUpperInvariant();
            if (status is not ("OK" or "ERROR" or "PENDIENTE")) continue;
            var id = f[i].Trim();
            if (id.Length is < 1 or > 128) continue;
            record = new CreditRecord(id, at, f[i + 2].Trim(), f[i + 3].Trim(), amount, status, string.Join("|", f.Skip(i + 7)));
            return true;
        }

        // Legacy format: date | bank | user | amount. It has no outcome field.
        if (f.Length == 4 && DateTimeOffset.TryParse(f[0], CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var legacyAt) &&
            decimal.TryParse(f[3], NumberStyles.Number, CultureInfo.InvariantCulture, out var legacyAmount))
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(line)));
            record = new CreditRecord("LEGACY-" + hash, legacyAt, f[1].Trim(), f[2].Trim(), legacyAmount, "SIN ESTADO", "Registro legado; no incluye resultado de ejecución.");
            return true;
        }
        return false;
    }
}


public sealed record PruneResult(int ImportedRecords, int RemovedRows, int PendingRowsLeft);