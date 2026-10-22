using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Invoices.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Invoice.Code
{
    public class DatabaseInfo
    {
        public string DatabaseName { get; set; } = string.Empty;
        public string ServerName { get; set; } = string.Empty;
        public string SqlVersion { get; set; } = string.Empty;
        public double? DatabaseSizeMb { get; set; }
        public int TotalTablesCount { get; set; }
        public long TotalRowsCount { get; set; }
        public DateTime ServerDateTime { get; set; }
    }

    public class DatabaseBackupStreamResult : IDisposable, IAsyncDisposable
    {
        public Stream Stream { get; }
        public string FileName { get; }
        public long? FileSize { get; }
        public string ContentType { get; }
        private readonly Action? _onDispose;
        private bool _disposed;

        public DatabaseBackupStreamResult(Stream stream, string fileName, long? fileSize, string contentType = "application/octet-stream", Action? onDispose = null)
        {
            Stream = stream ?? throw new ArgumentNullException(nameof(stream));
            FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
            FileSize = fileSize;
            ContentType = contentType;
            _onDispose = onDispose;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stream.Dispose();
            _onDispose?.Invoke();
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            await Stream.DisposeAsync();
            _onDispose?.Invoke();
        }
    }

    public interface IDatabaseBackupService
    {
        Task<DatabaseInfo> GetDatabaseInfoAsync(CancellationToken cancellationToken = default);
        Task<DatabaseBackupStreamResult> CreateBackupStreamAsync(string format = "zip", bool useCompression = true, CancellationToken cancellationToken = default);
    }

    public class DatabaseBackupService : IDatabaseBackupService
    {
        private readonly DataContext _context;
        private readonly ILogger<DatabaseBackupService> _logger;

        public DatabaseBackupService(DataContext context, ILogger<DatabaseBackupService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<DatabaseInfo> GetDatabaseInfoAsync(CancellationToken cancellationToken = default)
        {
            var conn = _context.Database.GetDbConnection();
            var shouldClose = false;
            if (conn.State != ConnectionState.Open)
            {
                await conn.OpenAsync(cancellationToken);
                shouldClose = true;
            }

            try
            {
                var info = new DatabaseInfo
                {
                    DatabaseName = conn.Database,
                    ServerName = conn.DataSource,
                    ServerDateTime = DateTime.Now
                };

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
SELECT
    CAST(SERVERPROPERTY('ProductVersion') AS NVARCHAR(128)) + ' - ' + CAST(SERVERPROPERTY('Edition') AS NVARCHAR(128)) AS VersionInfo,
    GETDATE() AS CurrentServerDate,
    (SELECT ISNULL((SUM(size) * 8.0) / 1024, 0) FROM sys.database_files) AS SizeInMb,
    (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0) AS TablesCount,
    (SELECT ISNULL(SUM(s.row_count), 0) FROM sys.tables t JOIN sys.dm_db_partition_stats s ON t.object_id = s.object_id WHERE s.index_id IN (0,1) AND t.is_ms_shipped = 0) AS TotalRows;";
                cmd.CommandTimeout = 60;

                using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    if (!reader.IsDBNull(0))
                        info.SqlVersion = reader.GetString(0);
                    if (!reader.IsDBNull(1))
                        info.ServerDateTime = reader.GetDateTime(1);
                    if (!reader.IsDBNull(2))
                        info.DatabaseSizeMb = Math.Round(Convert.ToDouble(reader.GetValue(2)), 2);
                    if (!reader.IsDBNull(3))
                        info.TotalTablesCount = Convert.ToInt32(reader.GetValue(3));
                    if (!reader.IsDBNull(4))
                        info.TotalRowsCount = Convert.ToInt64(reader.GetValue(4));
                }

                return info;
            }
            finally
            {
                if (shouldClose && conn.State == ConnectionState.Open)
                {
                    await conn.CloseAsync();
                }
            }
        }

        public async Task<DatabaseBackupStreamResult> CreateBackupStreamAsync(string format = "zip", bool useCompression = true, CancellationToken cancellationToken = default)
        {
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
            {
                await conn.OpenAsync(cancellationToken);
            }

            var dbName = conn.Database;
            if (string.IsNullOrWhiteSpace(dbName))
            {
                throw new InvalidOperationException("نام پایگاه داده در رشته اتصال مشخص نشده است.");
            }

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            // تلاش برای گرفتن یک فایل پشتیبان باینری (.bak) در پوشه دیتابیس سرور SQL به عنوان اسنپ‌شات سرور
            string? localBakPath = null;
            try
            {
                localBakPath = await TryCreateServerBakSnapshotAsync(conn, dbName, timestamp, useCompression, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ایجاد فایل پشتیبان .bak روی سرور با خطا مواجه شد؛ فرآیند با تهیه اسکریپت کامل ادامه می‌یابد.");
            }

            // در صورتی که کاربر صریحاً فرمت باینری .bak را خواسته و فایل به صورت محلی در دسترس باشد
            if (string.Equals(format, "bak", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(localBakPath) && File.Exists(localBakPath))
                {
                    var fi = new FileInfo(localBakPath);
                    var fs = new FileStream(localBakPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.DeleteOnClose);
                    _logger.LogInformation("فایل باینری .bak مستقیماً از دیسک باز شد ({Size} بایت).", fi.Length);
                    return new DatabaseBackupStreamResult(fs, $"{dbName}_{timestamp}.bak", fi.Length, "application/octet-stream");
                }
                else
                {
                    throw new InvalidOperationException("فایل پشتیبان .bak روی سرور ایجاد شد اما به دلیل تفکیک سرور دیتابیس یا محدودیت‌های دسترسی امنیتی، امکان دانلود مستقیم فایل باینری سرور وجود ندارد. لطفاً فرمت پشتیبان‌گیری فشرده (.zip) را انتخاب کنید.");
                }
            }

            // روش استاندارد و کاملاً مستقل از مجوزهای سیستمی: ایجاد اسکریپت کامل SQL و فشرده‌سازی در قالب فایل ZIP
            return await CreateCompleteDatabaseScriptZipAsync(conn, dbName, timestamp, cancellationToken);
        }

        private async Task<string?> TryCreateServerBakSnapshotAsync(DbConnection conn, string dbName, string timestamp, bool useCompression, CancellationToken cancellationToken)
        {
            string backupDir = string.Empty;
            using (var pathCmd = conn.CreateCommand())
            {
                pathCmd.CommandText = @"
DECLARE @path NVARCHAR(512) = CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS NVARCHAR(512));
IF @path IS NULL OR @path = ''
BEGIN
    SELECT TOP 1 @path = SUBSTRING(physical_name, 1, LEN(physical_name) - CHARINDEX('\', REVERSE(physical_name)) + 1)
    FROM sys.database_files WHERE type = 0;
END
SELECT ISNULL(@path, 'C:\temp\');";
                pathCmd.CommandTimeout = 60;
                var result = await pathCmd.ExecuteScalarAsync(cancellationToken);
                backupDir = result?.ToString() ?? @"C:\temp\";
            }

            if (!backupDir.EndsWith("\\") && !backupDir.EndsWith("/"))
            {
                backupDir += "\\";
            }

            var escapedDbName = dbName.Replace("]", "]]");
            var serverBackupFileName = $"{dbName}_Backup_{timestamp}_{Guid.NewGuid():N}.bak";
            var serverBackupPath = backupDir + serverBackupFileName;
            var backupDescription = $"Invoice System Full Backup - {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

            try
            {
                using var cmd = conn.CreateCommand();
                var compressionClause = useCompression ? ", COMPRESSION" : "";
                cmd.CommandText = $"BACKUP DATABASE [{escapedDbName}] TO DISK = @path WITH FORMAT, INIT, NAME = @name{compressionClause}, STATS = 10;";
                cmd.CommandTimeout = 900;

                var pPath = cmd.CreateParameter();
                pPath.ParameterName = "@path";
                pPath.Value = serverBackupPath;
                cmd.Parameters.Add(pPath);

                var pName = cmd.CreateParameter();
                pName.ParameterName = "@name";
                pName.Value = backupDescription;
                cmd.Parameters.Add(pName);

                await cmd.ExecuteNonQueryAsync(cancellationToken);
                _logger.LogInformation("پشتیبان‌گیری .bak روی دیسک سرور با موفقیت ایجاد شد: {Path}", serverBackupPath);
                return serverBackupPath;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "تلاش برای پشتیبان‌گیری مستقیم دیتابیس در مسیر {Path} ناموفق بود.", serverBackupPath);
                return null;
            }
        }

        private async Task<DatabaseBackupStreamResult> CreateCompleteDatabaseScriptZipAsync(DbConnection conn, string dbName, string timestamp, CancellationToken cancellationToken)
        {
            _logger.LogInformation("آغاز ایجاد اسکریپت کامل پایگاه داده {DbName}...", dbName);

            var tempSqlFile = Path.Combine(Path.GetTempPath(), $"{dbName}_{timestamp}_{Guid.NewGuid():N}.sql");
            var tempZipFile = Path.Combine(Path.GetTempPath(), $"{dbName}_Backup_{timestamp}_{Guid.NewGuid():N}.zip");
            var zipEntryFileName = $"{dbName}_Backup_{timestamp}.sql";
            var downloadFileName = $"{dbName}_Backup_{timestamp}.zip";

            try
            {
                using (var writer = new StreamWriter(tempSqlFile, false, Encoding.UTF8, 64 * 1024))
                {
                    await writer.WriteLineAsync("-- ==========================================================================");
                    await writer.WriteLineAsync($"-- پایگاه داده: {dbName}");
                    await writer.WriteLineAsync($"-- سرور: {conn.DataSource}");
                    await writer.WriteLineAsync($"-- تاریخ ایجاد: {DateTime.Now:yyyy-MM-dd HH:mm:ss} (شمسی: {MyDate.GetPersianDate(DateTime.Now)})");
                    await writer.WriteLineAsync("-- ==========================================================================");
                    await writer.WriteLineAsync("-- راهنما: پیش از اجرای این اسکریپت در سیستم خود، ابتدا دیتابیس مقصد را ایجاد کرده");
                    await writer.WriteLineAsync("-- و با دستور USE آن را انتخاب نمایید:");
                    await writer.WriteLineAsync("--");
                    await writer.WriteLineAsync($"-- CREATE DATABASE [{dbName}];");
                    await writer.WriteLineAsync("-- GO");
                    await writer.WriteLineAsync($"-- USE [{dbName}];");
                    await writer.WriteLineAsync("-- GO");
                    await writer.WriteLineAsync("-- ==========================================================================");
                    await writer.WriteLineAsync();
                    await writer.WriteLineAsync("IF DB_NAME() IN ('master', 'model', 'msdb', 'tempdb')");
                    await writer.WriteLineAsync("BEGIN");
                    await writer.WriteLineAsync("    RAISERROR(N'خطا: لطفاً ابتدا دیتابیس مقصد را با دستور USE انتخاب کنید و اسکریپت را بر روی دیتابیس master اجرا ننمایید.', 16, 1);");
                    await writer.WriteLineAsync("    RETURN;");
                    await writer.WriteLineAsync("END");
                    await writer.WriteLineAsync("GO");
                    await writer.WriteLineAsync();
                    await writer.WriteLineAsync("SET NOCOUNT ON;");
                    await writer.WriteLineAsync("SET XACT_ABORT ON;");
                    await writer.WriteLineAsync("GO");
                    await writer.WriteLineAsync();

                    // دریافت لیست جداول
                    var tables = new List<string>();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT name FROM sys.tables WHERE is_ms_shipped = 0 ORDER BY name;";
                        cmd.CommandTimeout = 60;
                        using var r = await cmd.ExecuteReaderAsync(cancellationToken);
                        while (await r.ReadAsync(cancellationToken))
                        {
                            tables.Add(r.GetString(0));
                        }
                    }

                    // گام اول: ایجاد ساختار جداول (DDL) شامل فیلدها، کلیدهای اصلی و قیود یکتا (Candidate Keys)
                    await writer.WriteLineAsync("-- ==========================================================================");
                    await writer.WriteLineAsync("-- گام ۱: ساختار جداول و کلیدهای اصلی و قیود یکتا (Tables, PKs, Unique Constraints)");
                    await writer.WriteLineAsync("-- ==========================================================================");
                    foreach (var tbl in tables)
                    {
                        var ddl = await GenerateTableDdlAsync(conn, tbl, cancellationToken);
                        await writer.WriteLineAsync(ddl);
                        await writer.WriteLineAsync("GO");
                        await writer.WriteLineAsync();
                    }

                    // گام دوم: ورود داده‌های جداول (Table Data) پیش از ایجاد ایندکس‌ها و کلیدهای خارجی
                    // با ورود داده‌ها قبل از فعال‌سازی Foreign Keys و Indexes، هیچ‌گونه خطای تداخل یا مسدودسازی داده به وجود نمی‌آید
                    await writer.WriteLineAsync("-- ==========================================================================");
                    await writer.WriteLineAsync("-- گام ۲: ورود داده‌های جداول (Table Data)");
                    await writer.WriteLineAsync("-- ==========================================================================");

                    long totalScriptedRows = 0;
                    foreach (var tbl in tables)
                    {
                        var scripted = await ScriptTableDataAsync(conn, tbl, writer, cancellationToken);
                        totalScriptedRows += scripted;
                        _logger.LogInformation("جدول {Table}: {Rows} رکورد در اسکریپت پشتیبان درج شد.", tbl, scripted);
                    }

                    // گام سوم: ایجاد تمام ایندکس‌های غیرکلاستر و یکتا با فیلتر (Indexes & Filtered Indexes)
                    // ایندکس‌هایی مانند IX_Users_ProfileId که دارای شرط فیلتر WHERE [ProfileId] IS NOT NULL هستند به درستی حفظ می‌شوند
                    await writer.WriteLineAsync("-- ==========================================================================");
                    await writer.WriteLineAsync("-- گام ۳: ایجاد ایندکس‌ها شامل ایندکس‌های یکتا و فیلتردار (Indexes & Filtered Indexes)");
                    await writer.WriteLineAsync("-- ==========================================================================");
                    var indexesScript = await GenerateIndexesScriptAsync(conn, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(indexesScript))
                    {
                        await writer.WriteLineAsync(indexesScript);
                        await writer.WriteLineAsync();
                    }

                    // گام چهارم: ایجاد روابط و کلیدهای خارجی (Foreign Keys) پس از تکمیل ورود داده‌ها و ایندکس‌ها
                    await writer.WriteLineAsync("-- ==========================================================================");
                    await writer.WriteLineAsync("-- گام ۴: ایجاد روابط و کلیدهای خارجی (Foreign Keys)");
                    await writer.WriteLineAsync("-- ==========================================================================");
                    var fkScript = await GenerateForeignKeysScriptAsync(conn, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(fkScript))
                    {
                        await writer.WriteLineAsync(fkScript);
                        await writer.WriteLineAsync("GO");
                        await writer.WriteLineAsync();
                    }

                    // گام پنجم: اعتبارسنجی تمام کلیدهای خارجی
                    await writer.WriteLineAsync();
                    await writer.WriteLineAsync("-- گام ۵: بررسی و اعتبارسنجی مجدد تمام کلیدهای خارجی");
                    await writer.WriteLineAsync("EXEC sp_msforeachtable \"ALTER TABLE ? WITH CHECK CHECK CONSTRAINT all\";");
                    await writer.WriteLineAsync("GO");
                    await writer.WriteLineAsync();
                    await writer.WriteLineAsync($"-- پایان موفقیت‌آمیز اسکریپت پشتیبان ({totalScriptedRows} رکورد در {tables.Count} جدول).");
                    await writer.FlushAsync();
                }

                _logger.LogInformation("فایل SQL با موفقیت ساخته شد. فشرده‌سازی در قالب فایل ZIP...");

                // فشرده‌سازی فایل SQL در یک آرشیو ZIP
                using (var zipArchive = ZipFile.Open(tempZipFile, ZipArchiveMode.Create))
                {
                    zipArchive.CreateEntryFromFile(tempSqlFile, zipEntryFileName, CompressionLevel.Optimal);
                }

                // حذف فایل میانی SQL
                try { File.Delete(tempSqlFile); } catch { }

                var zipFi = new FileInfo(tempZipFile);
                _logger.LogInformation("آرشیو ZIP فایل پشتیبان آماده شد ({Size} بایت).", zipFi.Length);

                var zipStream = new FileStream(tempZipFile, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.DeleteOnClose);
                return new DatabaseBackupStreamResult(zipStream, downloadFileName, zipFi.Length, "application/zip");
            }
            catch
            {
                // در صورت بروز خطا، فایل‌های موقت پاک شوند
                try { if (File.Exists(tempSqlFile)) File.Delete(tempSqlFile); } catch { }
                try { if (File.Exists(tempZipFile)) File.Delete(tempZipFile); } catch { }
                throw;
            }
        }

        private async Task<string> GenerateTableDdlAsync(DbConnection conn, string tableName, CancellationToken ct)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"IF OBJECT_ID(N'[{tableName}]', 'U') IS NULL");
            sb.AppendLine("BEGIN");
            sb.AppendLine($"    CREATE TABLE [{tableName}] (");

            var columns = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT
    c.name AS ColName,
    t.name AS TypeName,
    c.max_length,
    c.precision,
    c.scale,
    c.is_nullable,
    c.is_identity,
    dc.definition AS DefaultVal
FROM sys.columns c
JOIN sys.types t ON c.user_type_id = t.user_type_id
LEFT JOIN sys.default_constraints dc ON c.default_object_id = dc.object_id
WHERE c.object_id = OBJECT_ID(@tableName)
ORDER BY c.column_id;";
                var p = cmd.CreateParameter();
                p.ParameterName = "@tableName";
                p.Value = tableName;
                cmd.Parameters.Add(p);

                using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    var colName = r.GetString(0);
                    var typeName = r.GetString(1);
                    var maxLen = Convert.ToInt32(r.GetValue(2));
                    var prec = Convert.ToInt32(r.GetValue(3));
                    var scale = Convert.ToInt32(r.GetValue(4));
                    var isNullable = Convert.ToBoolean(r.GetValue(5));
                    var isIdentity = Convert.ToBoolean(r.GetValue(6));
                    var defVal = r.IsDBNull(7) ? null : r.GetString(7);

                    string typeStr = typeName;
                    if (typeName == "nvarchar" || typeName == "nchar")
                    {
                        typeStr = maxLen == -1 ? $"{typeName}(max)" : $"{typeName}({maxLen / 2})";
                    }
                    else if (typeName == "varchar" || typeName == "char" || typeName == "varbinary" || typeName == "binary")
                    {
                        typeStr = maxLen == -1 ? $"{typeName}(max)" : $"{typeName}({maxLen})";
                    }
                    else if (typeName == "decimal" || typeName == "numeric")
                    {
                        typeStr = $"{typeName}({prec},{scale})";
                    }
                    else if (typeName == "datetime2" || typeName == "time")
                    {
                        typeStr = $"{typeName}({scale})";
                    }

                    var colDef = $"        [{colName}] {typeStr}";
                    if (isIdentity)
                    {
                        colDef += " IDENTITY(1,1)";
                    }
                    colDef += isNullable ? " NULL" : " NOT NULL";
                    if (!string.IsNullOrEmpty(defVal))
                    {
                        colDef += $" DEFAULT {defVal}";
                    }

                    columns.Add(colDef);
                }
            }

            // Primary Key
            var pkCols = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT c.name
FROM sys.indexes i
JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
WHERE i.object_id = OBJECT_ID(@tableName) AND i.is_primary_key = 1
ORDER BY ic.key_ordinal;";
                var p = cmd.CreateParameter();
                p.ParameterName = "@tableName";
                p.Value = tableName;
                cmd.Parameters.Add(p);

                using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    pkCols.Add($"[{r.GetString(0)}]");
                }
            }

            if (pkCols.Count > 0)
            {
                columns.Add($"        CONSTRAINT [PK_{tableName}] PRIMARY KEY ({string.Join(", ", pkCols)})");
            }

            // Unique Constraints (قیود یکتا و کلیدهای کاندیدا مانند AK_CurrencyCodes_AlphabetCode و AK_MeasureUnits_Code)
            var uqConstraints = new Dictionary<string, List<string>>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT i.name AS IndexName, c.name AS ColName
FROM sys.indexes i
JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
WHERE i.object_id = OBJECT_ID(@tableName) AND i.is_unique_constraint = 1 AND i.is_primary_key = 0
ORDER BY i.name, ic.key_ordinal;";
                var p = cmd.CreateParameter();
                p.ParameterName = "@tableName";
                p.Value = tableName;
                cmd.Parameters.Add(p);

                using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    var idxName = r.GetString(0);
                    var colName = r.GetString(1);
                    if (!uqConstraints.ContainsKey(idxName))
                    {
                        uqConstraints[idxName] = new List<string>();
                    }
                    uqConstraints[idxName].Add($"[{colName}]");
                }
            }

            foreach (var kvp in uqConstraints)
            {
                columns.Add($"        CONSTRAINT [{kvp.Key}] UNIQUE ({string.Join(", ", kvp.Value)})");
            }

            sb.AppendLine(string.Join(",\n", columns));
            sb.AppendLine("    );");
            sb.AppendLine("END;");

            return sb.ToString();
        }

        private class IndexDef
        {
            public string Name { get; set; } = string.Empty;
            public string TableName { get; set; } = string.Empty;
            public bool IsUnique { get; set; }
            public string TypeDesc { get; set; } = "NONCLUSTERED";
            public bool HasFilter { get; set; }
            public string? FilterDefinition { get; set; }
            public List<string> KeyColumns { get; set; } = new();
            public List<string> IncludedColumns { get; set; } = new();
        }

        private async Task<string> GenerateIndexesScriptAsync(DbConnection conn, CancellationToken ct)
        {
            var sb = new StringBuilder();
            var indexDict = new Dictionary<string, IndexDef>(StringComparer.OrdinalIgnoreCase);

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT
    OBJECT_NAME(i.object_id) AS TableName,
    i.name AS IndexName,
    i.is_unique AS IsUnique,
    i.type_desc AS TypeDesc,
    i.has_filter AS HasFilter,
    i.filter_definition AS FilterDefinition,
    c.name AS ColumnName,
    ic.is_descending_key AS IsDescending,
    ic.is_included_column AS IsIncluded
FROM sys.indexes i
JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
JOIN sys.tables t ON i.object_id = t.object_id
WHERE t.is_ms_shipped = 0
  AND i.is_primary_key = 0
  AND i.is_unique_constraint = 0
  AND i.name IS NOT NULL
ORDER BY TableName, i.name, ic.is_included_column, ic.key_ordinal, ic.index_column_id;";

                using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    var tableName = r.GetString(0);
                    var indexName = r.GetString(1);
                    var isUnique = Convert.ToBoolean(r.GetValue(2));
                    var typeDesc = r.IsDBNull(3) ? "NONCLUSTERED" : r.GetString(3);
                    var hasFilter = Convert.ToBoolean(r.GetValue(4));
                    var filterDef = r.IsDBNull(5) ? null : Convert.ToString(r.GetValue(5));
                    var colName = r.GetString(6);
                    var isDesc = Convert.ToBoolean(r.GetValue(7));
                    var isIncluded = Convert.ToBoolean(r.GetValue(8));

                    var key = $"{tableName}.{indexName}";
                    if (!indexDict.TryGetValue(key, out var idx))
                    {
                        idx = new IndexDef
                        {
                            TableName = tableName,
                            Name = indexName,
                            IsUnique = isUnique,
                            TypeDesc = typeDesc,
                            HasFilter = hasFilter,
                            FilterDefinition = filterDef
                        };
                        indexDict[key] = idx;
                    }

                    if (isIncluded)
                    {
                        idx.IncludedColumns.Add($"[{colName}]");
                    }
                    else
                    {
                        idx.KeyColumns.Add(isDesc ? $"[{colName}] DESC" : $"[{colName}]");
                    }
                }
            }

            foreach (var idx in indexDict.Values)
            {
                if (idx.KeyColumns.Count == 0) continue;

                var uniqueStr = idx.IsUnique ? "UNIQUE " : "";
                var typeStr = idx.TypeDesc == "CLUSTERED" ? "CLUSTERED " : "NONCLUSTERED ";
                var keyColsStr = string.Join(", ", idx.KeyColumns);
                var includeClause = idx.IncludedColumns.Count > 0 ? $" INCLUDE ({string.Join(", ", idx.IncludedColumns)})" : "";
                var filterClause = (idx.HasFilter && !string.IsNullOrWhiteSpace(idx.FilterDefinition))
                    ? $" WHERE {idx.FilterDefinition.Trim()}"
                    : "";

                sb.AppendLine($"IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = '{idx.Name}' AND object_id = OBJECT_ID(N'[{idx.TableName}]'))");
                sb.AppendLine("BEGIN");
                sb.AppendLine($"    CREATE {uniqueStr}{typeStr}INDEX [{idx.Name}] ON [{idx.TableName}] ({keyColsStr}){includeClause}{filterClause};");
                sb.AppendLine("END;");
                sb.AppendLine("GO");
            }

            return sb.ToString();
        }

        private class ForeignKeyDef
        {
            public string Name { get; set; } = string.Empty;
            public string ParentTable { get; set; } = string.Empty;
            public string ReferencedTable { get; set; } = string.Empty;
            public string DeleteAction { get; set; } = string.Empty;
            public string UpdateAction { get; set; } = string.Empty;
            public List<string> ParentColumns { get; set; } = new();
            public List<string> ReferencedColumns { get; set; } = new();
        }

        private async Task<string> GenerateForeignKeysScriptAsync(DbConnection conn, CancellationToken ct)
        {
            var sb = new StringBuilder();
            var fkDict = new Dictionary<string, ForeignKeyDef>();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT
    fk.name AS ForeignKeyName,
    OBJECT_NAME(fk.parent_object_id) AS ParentTable,
    cp.name AS ParentColumn,
    OBJECT_NAME(fk.referenced_object_id) AS ReferencedTable,
    cr.name AS ReferencedColumn,
    fk.delete_referential_action_desc AS DeleteAction,
    fk.update_referential_action_desc AS UpdateAction
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
JOIN sys.columns cp ON fkc.parent_object_id = cp.object_id AND fkc.parent_column_id = cp.column_id
JOIN sys.columns cr ON fkc.referenced_object_id = cr.object_id AND fkc.referenced_column_id = cr.column_id
WHERE fk.is_ms_shipped = 0
ORDER BY fk.name, fkc.constraint_column_id;";

                using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    var fkName = r.GetString(0);
                    var pTable = r.GetString(1);
                    var pCol = r.GetString(2);
                    var rTable = r.GetString(3);
                    var rCol = r.GetString(4);
                    var delAction = r.GetString(5);
                    var updAction = r.GetString(6);

                    if (!fkDict.TryGetValue(fkName, out var fkDef))
                    {
                        fkDef = new ForeignKeyDef
                        {
                            Name = fkName,
                            ParentTable = pTable,
                            ReferencedTable = rTable,
                            DeleteAction = delAction,
                            UpdateAction = updAction
                        };
                        fkDict[fkName] = fkDef;
                    }

                    fkDef.ParentColumns.Add($"[{pCol}]");
                    fkDef.ReferencedColumns.Add($"[{rCol}]");
                }
            }

            foreach (var fk in fkDict.Values)
            {
                var delClause = fk.DeleteAction switch
                {
                    "CASCADE" => " ON DELETE CASCADE",
                    "SET_NULL" => " ON DELETE SET NULL",
                    "SET_DEFAULT" => " ON DELETE SET DEFAULT",
                    _ => ""
                };

                var updClause = fk.UpdateAction switch
                {
                    "CASCADE" => " ON UPDATE CASCADE",
                    "SET_NULL" => " ON UPDATE SET NULL",
                    "SET_DEFAULT" => " ON UPDATE SET DEFAULT",
                    _ => ""
                };

                var pColsStr = string.Join(", ", fk.ParentColumns);
                var rColsStr = string.Join(", ", fk.ReferencedColumns);

                sb.AppendLine($"IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = '{fk.Name}')");
                sb.AppendLine("BEGIN");
                sb.AppendLine($"    ALTER TABLE [{fk.ParentTable}] WITH CHECK ADD CONSTRAINT [{fk.Name}] FOREIGN KEY ({pColsStr}) REFERENCES [{fk.ReferencedTable}] ({rColsStr}){delClause}{updClause};");
                sb.AppendLine($"    ALTER TABLE [{fk.ParentTable}] CHECK CONSTRAINT [{fk.Name}];");
                sb.AppendLine("END;");
                sb.AppendLine("GO");
            }

            return sb.ToString();
        }

        private async Task<long> ScriptTableDataAsync(DbConnection conn, string tableName, StreamWriter writer, CancellationToken ct)
        {
            bool hasIdentity = false;
            using (var idCmd = conn.CreateCommand())
            {
                idCmd.CommandText = "SELECT OBJECTPROPERTY(OBJECT_ID(@tableName), 'TableHasIdentity');";
                var p = idCmd.CreateParameter();
                p.ParameterName = "@tableName";
                p.Value = tableName;
                idCmd.Parameters.Add(p);
                var res = await idCmd.ExecuteScalarAsync(ct);
                hasIdentity = (Convert.ToInt32(res) == 1);
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT * FROM [{tableName}];";
            cmd.CommandTimeout = 600;

            using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
            if (!reader.HasRows)
            {
                return 0;
            }

            var colNames = new List<string>();
            var colTypes = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                colNames.Add($"[{reader.GetName(i)}]");
                colTypes.Add(reader.GetDataTypeName(i).ToLowerInvariant());
            }

            var columnList = string.Join(", ", colNames);

            await writer.WriteLineAsync($"-- داده‌های جدول [{tableName}]");
            if (hasIdentity)
            {
                await writer.WriteLineAsync($"SET IDENTITY_INSERT [{tableName}] ON;");
            }

            long rowCount = 0;
            int batchCount = 0;
            const int batchSize = 500;

            while (await reader.ReadAsync(ct))
            {
                rowCount++;
                if (batchCount == 0)
                {
                    await writer.WriteAsync($"INSERT INTO [{tableName}] ({columnList}) VALUES \n(");
                }
                else
                {
                    await writer.WriteAsync(",\n(");
                }

                var rowValues = new string[reader.FieldCount];
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    if (await reader.IsDBNullAsync(i, ct))
                    {
                        rowValues[i] = "NULL";
                    }
                    else
                    {
                        rowValues[i] = FormatSqlValue(reader.GetValue(i), colTypes[i]);
                    }
                }

                await writer.WriteAsync(string.Join(", ", rowValues));
                await writer.WriteAsync(")");

                batchCount++;
                if (batchCount >= batchSize)
                {
                    await writer.WriteLineAsync(";");
                    await writer.WriteLineAsync("GO");
                    batchCount = 0;
                }
            }

            if (batchCount > 0)
            {
                await writer.WriteLineAsync(";");
                await writer.WriteLineAsync("GO");
            }

            if (hasIdentity)
            {
                await writer.WriteLineAsync($"SET IDENTITY_INSERT [{tableName}] OFF;");
                await writer.WriteLineAsync("GO");
            }

            await writer.WriteLineAsync();
            return rowCount;
        }

        private static string FormatSqlValue(object val, string typeName)
        {
            switch (typeName)
            {
                case "bit":
                    return (bool)val ? "1" : "0";

                case "int":
                case "bigint":
                case "smallint":
                case "tinyint":
                    return val.ToString()!;

                case "decimal":
                case "numeric":
                case "money":
                case "smallmoney":
                    return Convert.ToDecimal(val).ToString(CultureInfo.InvariantCulture);

                case "float":
                case "real":
                    return Convert.ToDouble(val).ToString("R", CultureInfo.InvariantCulture);

                case "datetime":
                case "datetime2":
                case "smalldatetime":
                case "date":
                    return $"'{((DateTime)val):yyyy-MM-dd HH:mm:ss.fffffff}'";

                case "time":
                    return $"'{((TimeSpan)val):hh\\:mm\\:ss\\.fffffff}'";

                case "uniqueidentifier":
                    return $"'{val}'";

                case "binary":
                case "varbinary":
                case "image":
                    var bytes = (byte[])val;
                    return "0x" + BitConverter.ToString(bytes).Replace("-", "");

                default:
                    var str = val.ToString() ?? string.Empty;
                    return "N'" + str.Replace("'", "''") + "'";
            }
        }
    }
}
