using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using EFCore.OpenEdge.FunctionalTests.TestUtilities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit.Abstractions;

namespace EFCore.OpenEdge.FunctionalTests.Integration
{
    /// <summary>
    /// Discovers the columns of a real OpenEdge database from its catalog and runs every data type through
    /// the provider, so no fixed schema is needed. It only reads, limits every query and stops at a time
    /// budget, and it never reports row values. Table and column names appear in the report.
    /// Tune it with OPENEDGE_SWEEP_COLUMNS_PER_TYPE, OPENEDGE_SWEEP_ROWS, OPENEDGE_SWEEP_MINUTES and
    /// OPENEDGE_SWEEP_QUERY_TIMEOUT_SECONDS.
    /// </summary>
    public class LiveTypeSweepTests
    {
        private const string ReadCheck = "read";
        private const string ProjectCheck = "project";
        private const string IsNullCheck = "is-null";
        private const string IsNotNullCheck = "is-not-null";
        private const string EqualToParameterCheck = "equal-parameter";
        private const string EqualToLiteralCheck = "equal-literal";
        private const string InCheck = "in-list";
        private const string OrderedCheck = "order-by";

        private const string CatalogQuery = """
            SELECT t."_Owner", t."_File-Name", f."_Field-Name", f."_Data-Type", f."_Extent"
            FROM pub."_Field" f
            INNER JOIN pub."_File" t ON t.rowid = f."_File-recid"
            WHERE t."_Tbl-Type" = 'T'
            """;

        private static readonly string[] Checks =
        [
            ReadCheck, ProjectCheck, IsNullCheck, IsNotNullCheck,
            EqualToParameterCheck, EqualToLiteralCheck, InCheck, OrderedCheck
        ];

        private static readonly Lazy<List<ColumnTarget>> Catalog = new(ReadCatalog);

        private static readonly MethodInfo SweepColumnMethod = typeof(LiveTypeSweepTests)
            .GetMethod(nameof(SweepColumn), BindingFlags.NonPublic | BindingFlags.Instance)!;

        private readonly ITestOutputHelper _output;
        private readonly int _columnsPerType = ReadSetting("OPENEDGE_SWEEP_COLUMNS_PER_TYPE", 50);
        private readonly int _rowsPerColumn = ReadSetting("OPENEDGE_SWEEP_ROWS", 20);
        private readonly int _queryTimeoutSeconds = ReadSetting("OPENEDGE_SWEEP_QUERY_TIMEOUT_SECONDS", 30);
        private readonly TimeSpan _budget = TimeSpan.FromMinutes(ReadSetting("OPENEDGE_SWEEP_MINUTES", 15));
        private string _connectionLostOn;

        public LiveTypeSweepTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private enum CheckOutcome
        {
            Passed,
            Failed,
            TimedOut,
            Denied,
            Missing,
            ConnectionLost
        }

        private sealed class TypeSummary(string label, Type clrType)
        {
            public string Label { get; } = label;
            public Type ClrType { get; } = clrType;
            public int ColumnCount { get; set; }
            public int ColumnsWithoutData { get; set; }
            public Dictionary<(string Check, CheckOutcome Outcome), int> Counts { get; } = [];
            public List<string> Failures { get; } = [];

            public void Record(string check, CheckOutcome outcome)
            {
                Counts[(check, outcome)] = Counts.GetValueOrDefault((check, outcome)) + 1;
            }
        }

        [LiveDatabaseFact]
        public void Every_Catalog_Data_Type_Should_Have_A_Type_Mapping()
        {
            var typeMappingSource = CreateTypeMappingSource();

            var unmappedTypes = Catalog.Value
                .GroupBy(column => column.StoreType, StringComparer.OrdinalIgnoreCase)
                .Where(group => typeMappingSource.FindMapping(group.Key) == null)
                .Select(group => $"{group.Key} ({group.Count()} columns)")
                .ToList();

            unmappedTypes.Should().BeEmpty();
        }

        [LiveDatabaseFact]
        public void Every_Data_Type_Should_Round_Trip_Through_The_Provider()
        {
            var typeMappingSource = CreateTypeMappingSource();
            var targets = SelectTargets(Catalog.Value);
            var summaries = new Dictionary<string, TypeSummary>(StringComparer.OrdinalIgnoreCase);
            var stopwatch = Stopwatch.StartNew();
            var sweptCount = 0;

            foreach (var target in targets)
            {
                // Never keep hitting a server that dropped the connection.
                if (stopwatch.Elapsed > _budget || _connectionLostOn != null)
                {
                    break;
                }

                var mapping = typeMappingSource.FindMapping(target.StoreType);
                if (mapping == null)
                {
                    continue;
                }

                var label = target.Extent > 0 ? $"{target.StoreType}[]" : target.StoreType;
                if (!summaries.TryGetValue(label, out var summary))
                {
                    summary = new TypeSummary(label, mapping.ClrType);
                    summaries.Add(label, summary);
                }

                SweepColumnMethod
                    .MakeGenericMethod(ToNullableType(mapping.ClrType))
                    .Invoke(this, [target, summary]);
                sweptCount++;
            }

            var report = CreateReport(summaries.Values, sweptCount, targets.Count, stopwatch.Elapsed);
            var reportPath = Path.Combine(AppContext.BaseDirectory, "type-sweep-report.txt");
            File.WriteAllText(reportPath, report);
            _output.WriteLine(report);
            _output.WriteLine($"Report written to {reportPath}");

            sweptCount.Should().BePositive();
            _connectionLostOn.Should().BeNull("the sweep stops as soon as the server drops a connection");
            summaries.Values.Sum(summary => summary.Failures.Count).Should().Be(0, $"the failures are listed in {reportPath}");
        }

        private void SweepColumn<T>(ColumnTarget target, TypeSummary summary)
        {
            summary.ColumnCount++;

            using var context = new ColumnProbeContext<T>(target, LiveDatabaseFactAttribute.ConnectionString);
            context.Database.SetCommandTimeout(_queryTimeoutSeconds);

            List<ProbeRow<T>> sampledRows = null;
            if (!RunCheck(summary, target, ReadCheck, () =>
            {
                sampledRows = ColumnProbeQueries<T>.Read(context.Rows, _rowsPerColumn).ToList();
            }))
            {
                return;
            }

            RunCheck(summary, target, ProjectCheck, () => ColumnProbeQueries<T>.Project(context.Rows, _rowsPerColumn).ToList());
            RunCheck(summary, target, IsNullCheck, () => ColumnProbeQueries<T>.IsNull(context.Rows).ToList());

            var values = GetDistinctValues(sampledRows);
            RunCheck(summary, target, IsNotNullCheck, () =>
            {
                var rows = ColumnProbeQueries<T>.IsNotNull(context.Rows, _rowsPerColumn).ToList();
                if (rows.Count == 0 && values.Count > 0)
                {
                    throw new InvalidOperationException("IS NOT NULL returned no rows, but the column has values.");
                }

                if (values.Count == 0)
                {
                    values = GetDistinctValues(rows);
                }
            });

            if (values.Count == 0)
            {
                summary.ColumnsWithoutData++;
                return;
            }

            // Comparing a LOB column is rejected, and with a parameter it closes the connection on the
            // server, so these columns are only ever read. Byte arrays have no value equality here.
            if (IsLargeObject(target.StoreType) || typeof(T) == typeof(byte[]))
            {
                return;
            }

            RunCheck(summary, target, EqualToParameterCheck, () =>
                ExpectRows(ColumnProbeQueries<T>.EqualToParameter(context.Rows, values[0]).ToList()));
            RunCheck(summary, target, EqualToLiteralCheck, () =>
                ExpectRows(ColumnProbeQueries<T>.EqualToLiteral(context.Rows, values[0]).ToList()));
            RunCheck(summary, target, InCheck, () =>
                ExpectRows(ColumnProbeQueries<T>.In(context.Rows, values).ToList()));
            RunCheck(summary, target, OrderedCheck, () =>
                ExpectRows(ColumnProbeQueries<T>.Ordered(context.Rows, _rowsPerColumn).ToList()));
        }

        private static bool IsLargeObject(string storeType)
            => storeType.Equals("clob", StringComparison.OrdinalIgnoreCase)
                || storeType.Equals("blob", StringComparison.OrdinalIgnoreCase);

        private static void ExpectRows<T>(List<ProbeRow<T>> rows)
        {
            if (rows.Count == 0)
            {
                throw new InvalidOperationException("The query returned no rows, but a sampled value should match.");
            }
        }

        private static List<T> GetDistinctValues<T>(List<ProbeRow<T>> rows)
            => rows.Select(row => row.Value)
                .Where(value => value != null)
                .Distinct()
                .Take(3)
                .ToList();

        private bool RunCheck(TypeSummary summary, ColumnTarget target, string check, Action run)
        {
            if (_connectionLostOn != null)
            {
                return false;
            }

            try
            {
                run();
                summary.Record(check, CheckOutcome.Passed);
                return true;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                var message = Sanitize(exception);
                var outcome = Classify(message);
                summary.Record(check, outcome);

                if (outcome == CheckOutcome.ConnectionLost)
                {
                    _connectionLostOn = $"{summary.Label} {check} on {target.Table}.{target.Column}: {message}";
                }
                else if (outcome is CheckOutcome.Failed or CheckOutcome.Missing)
                {
                    summary.Failures.Add($"{summary.Label} {check} on {target.Table}.{target.Column}: {message}");
                }

                return false;
            }
        }

        private static CheckOutcome Classify(string message)
        {
            if (message.Contains("[08S01]", StringComparison.Ordinal)
                || message.Contains("[08001]", StringComparison.Ordinal))
            {
                return CheckOutcome.ConnectionLost;
            }

            if (message.Contains("[42S02]", StringComparison.Ordinal))
            {
                return CheckOutcome.Missing;
            }

            if (message.Contains("Access denied", StringComparison.OrdinalIgnoreCase))
            {
                return CheckOutcome.Denied;
            }

            return message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                || message.Contains("timed out", StringComparison.OrdinalIgnoreCase)
                || message.Contains("HYT00", StringComparison.OrdinalIgnoreCase)
                    ? CheckOutcome.TimedOut
                    : CheckOutcome.Failed;
        }

        // Driver and parser messages can quote the value they failed on, and row values must stay out of the report.
        private static string Sanitize(Exception exception)
        {
            var innermost = exception.GetBaseException();
            var firstLine = innermost.Message.Split('\n')[0].Trim();
            var redacted = Regex.Replace(firstLine, "'[^']*'", "'...'");

            return $"{innermost.GetType().Name}: {redacted[..Math.Min(redacted.Length, 220)]}";
        }

        /// <summary>
        /// Picks up to the configured number of columns per data type, spread over as many tables as possible.
        /// Array fields count as their own type, because SQL exposes them differently from single values.
        /// </summary>
        private List<ColumnTarget> SelectTargets(IEnumerable<ColumnTarget> catalog)
            => catalog
                .GroupBy(column => (Type: column.StoreType.ToLowerInvariant(), IsArray: column.Extent > 0))
                .OrderBy(group => group.Key.Type)
                .ThenBy(group => group.Key.IsArray)
                .SelectMany(group => group
                    .GroupBy(column => column.Table)
                    .OrderBy(table => table.Key, StringComparer.Ordinal)
                    .SelectMany(table => table
                        .OrderBy(column => column.Column, StringComparer.Ordinal)
                        .Select((column, position) => (Column: column, Position: position)))
                    .OrderBy(entry => entry.Position)
                    .Take(_columnsPerType)
                    .Select(entry => entry.Column))
                .ToList();

        private string CreateReport(
            IEnumerable<TypeSummary> summaries,
            int sweptCount,
            int targetCount,
            TimeSpan elapsed)
        {
            var report = new StringBuilder();
            report.AppendLine($"Swept {sweptCount} of {targetCount} selected columns in {elapsed:hh\\:mm\\:ss}.");

            if (_connectionLostOn != null)
            {
                report.AppendLine($"ABORTED, connection lost during {_connectionLostOn}");
            }

            foreach (var summary in summaries.OrderBy(summary => summary.Label, StringComparer.OrdinalIgnoreCase))
            {
                report.AppendLine();
                report.AppendLine($"{summary.Label} -> {summary.ClrType.Name}: {summary.ColumnCount} columns, {summary.ColumnsWithoutData} without data");

                foreach (var check in Checks)
                {
                    var counts = Enum.GetValues<CheckOutcome>()
                        .Select(outcome => (Outcome: outcome, Count: summary.Counts.GetValueOrDefault((check, outcome))))
                        .Where(entry => entry.Count > 0)
                        .Select(entry => $"{entry.Outcome.ToString().ToLowerInvariant()} {entry.Count}")
                        .ToList();

                    report.AppendLine($"  {check}: {(counts.Count > 0 ? string.Join(", ", counts) : "not run")}");
                }

                foreach (var failure in summary.Failures.GroupBy(GetFailureKey).Select(group => group.First()))
                {
                    report.AppendLine($"  FAILED {failure}");
                }
            }

            return report.ToString();
        }

        // Groups failures with the same check and error, so one bug does not fill the report.
        private static string GetFailureKey(string failure)
            => Regex.Replace(failure, " on [^:]+:", ":");

        private static Type ToNullableType(Type clrType)
            => clrType.IsValueType ? typeof(Nullable<>).MakeGenericType(clrType) : clrType;

        private static IRelationalTypeMappingSource CreateTypeMappingSource()
        {
            var target = new ColumnTarget("pub", "_File", "_File-Name", "character", 0);
            using var context = new ColumnProbeContext<string>(target, LiveDatabaseFactAttribute.ConnectionString);

            return context.GetService<IRelationalTypeMappingSource>();
        }

        private static List<ColumnTarget> ReadCatalog()
        {
            var columns = new List<ColumnTarget>();

            using var connection = new OdbcConnection(LiveDatabaseFactAttribute.ConnectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = CatalogQuery;

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(new ColumnTarget(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    Convert.ToInt32(reader.GetValue(4))));
            }

            return columns;
        }

        private static int ReadSetting(string variable, int defaultValue)
            => int.TryParse(Environment.GetEnvironmentVariable(variable), out var value) ? value : defaultValue;
    }
}
