using System;
using System.Collections.Generic;
using System.Linq;
using EFCore.OpenEdge.FunctionalTests.TestUtilities;
using EntityFrameworkCore.OpenEdge.Diagnostics;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EFCore.OpenEdge.FunctionalTests.Unit.Query
{
    /// <summary>
    /// Verifies without a database that queries which would compare, sort or pattern-match a large object
    /// are stopped before any SQL is sent, while reading and null checks keep working.
    /// </summary>
    public class OpenEdgeLargeObjectGuardTests
    {
        private static ColumnProbeContext<T> CreateContext<T>(string storeType)
            => new(new ColumnTarget("pub", "orders", "notes", storeType, 0), "dsn=none");

        private static void ShouldBeBlocked(IQueryable query, string storeType, string operation)
        {
            Action generateSql = () => query.ToQueryString();

            var exception = generateSql.Should().Throw<OpenEdgeLargeObjectOperationException>().Which;
            exception.ColumnName.Should().Be("notes");
            exception.StoreType.Should().Be(storeType);
            exception.Operation.Should().Be(operation);
        }

        [Fact]
        public void Clob_Equality_With_Parameter_Should_Be_Blocked()
        {
            using var context = CreateContext<string>("clob");

            ShouldBeBlocked(ColumnProbeQueries<string>.EqualToParameter(context.Rows, "text"), "clob", "a comparison");
        }

        [Fact]
        public void Clob_Equality_With_Literal_Should_Be_Blocked()
        {
            using var context = CreateContext<string>("clob");

            ShouldBeBlocked(ColumnProbeQueries<string>.EqualToLiteral(context.Rows, "text"), "clob", "a comparison");
        }

        [Fact]
        public void Clob_In_List_Should_Be_Blocked()
        {
            using var context = CreateContext<string>("clob");
            var values = new List<string> { "first", "second" };

            ShouldBeBlocked(ColumnProbeQueries<string>.In(context.Rows, values), "clob", "an IN list");
        }

        [Fact]
        public void Clob_Order_By_Should_Be_Blocked()
        {
            using var context = CreateContext<string>("clob");

            ShouldBeBlocked(ColumnProbeQueries<string>.Ordered(context.Rows, 20), "clob", "ORDER BY");
        }

        [Fact]
        public void Clob_StartsWith_Should_Be_Blocked()
        {
            using var context = CreateContext<string>("clob");
            var prefix = "text";

            ShouldBeBlocked(context.Rows.Where(row => row.Value.StartsWith(prefix)), "clob", "LIKE");
        }

        [Fact]
        public void Blob_Equality_With_Parameter_Should_Be_Blocked()
        {
            using var context = CreateContext<byte[]>("blob");

            ShouldBeBlocked(ColumnProbeQueries<byte[]>.EqualToParameter(context.Rows, [1, 2, 3]), "blob", "a comparison");
        }

        [Fact]
        public void Clob_Read_And_Null_Checks_Should_Still_Translate()
        {
            using var context = CreateContext<string>("clob");

            IQueryable[] queries =
            [
                ColumnProbeQueries<string>.Read(context.Rows, 20),
                ColumnProbeQueries<string>.Project(context.Rows, 20),
                ColumnProbeQueries<string>.IsNull(context.Rows),
                ColumnProbeQueries<string>.IsNotNull(context.Rows, 20)
            ];

            foreach (var query in queries)
            {
                query.ToQueryString().Should().Contain("\"notes\"");
            }
        }

        [Fact]
        public void Character_Comparisons_Should_Still_Translate()
        {
            using var context = CreateContext<string>("character");

            ColumnProbeQueries<string>.EqualToParameter(context.Rows, "text").ToQueryString()
                .Should().Contain("\"notes\" = ?");
            ColumnProbeQueries<string>.Ordered(context.Rows, 20).ToQueryString()
                .Should().Contain("ORDER BY");
        }
    }
}
