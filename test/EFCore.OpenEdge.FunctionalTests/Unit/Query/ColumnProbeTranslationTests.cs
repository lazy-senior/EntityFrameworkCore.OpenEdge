using System;
using System.Collections.Generic;
using System.Linq;
using EFCore.OpenEdge.FunctionalTests.TestUtilities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EFCore.OpenEdge.FunctionalTests.Unit.Query
{
    /// <summary>
    /// Verifies without a database that the live type sweep can translate its queries for every CLR type.
    /// </summary>
    public class ColumnProbeTranslationTests
    {
        private static void VerifyTranslation<T>(string storeType, T firstValue, T secondValue)
        {
            var target = new ColumnTarget("pub", "orders", "order-value", storeType, 0);
            using var context = new ColumnProbeContext<T>(target, "dsn=none");

            IQueryable[] queries =
            [
                ColumnProbeQueries<T>.Read(context.Rows, 20),
                ColumnProbeQueries<T>.Project(context.Rows, 20),
                ColumnProbeQueries<T>.IsNull(context.Rows),
                ColumnProbeQueries<T>.IsNotNull(context.Rows, 20),
                ColumnProbeQueries<T>.EqualToParameter(context.Rows, firstValue),
                ColumnProbeQueries<T>.EqualToLiteral(context.Rows, firstValue),
                ColumnProbeQueries<T>.In(context.Rows, new List<T> { firstValue, secondValue }),
                ColumnProbeQueries<T>.Ordered(context.Rows, 20)
            ];

            foreach (var query in queries)
            {
                query.ToQueryString().Should().Contain("\"pub\".\"orders\"").And.Contain("\"order-value\"");
            }
        }

        [Fact]
        public void Queries_Should_Translate_For_Numeric_Types()
        {
            VerifyTranslation<int?>("integer", 1, 2);
            VerifyTranslation<long?>("int64", 1L, 2L);
            VerifyTranslation<short?>("smallint", (short)1, (short)2);
            VerifyTranslation<byte?>("tinyint", (byte)1, (byte)2);
            VerifyTranslation<decimal?>("decimal", 1.5m, 2.5m);
            VerifyTranslation<double?>("double precision", 1.5d, 2.5d);
            VerifyTranslation<float?>("real", 1.5f, 2.5f);
        }

        [Fact]
        public void Queries_Should_Translate_For_Text_And_Logical_Types()
        {
            VerifyTranslation("character", "first", "second");
            VerifyTranslation("recid", "1", "2");
            VerifyTranslation<bool?>("logical", true, false);
        }

        [Fact]
        public void Queries_Should_Translate_For_Date_And_Time_Types()
        {
            var moment = new DateTime(2024, 1, 2, 3, 4, 5);
            var offsetMoment = new DateTimeOffset(moment, TimeSpan.FromHours(2));

            VerifyTranslation<DateOnly?>("date", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 3));
            VerifyTranslation<DateTime?>("datetime", moment, moment.AddDays(1));
            VerifyTranslation<DateTime?>("timestamp", moment, moment.AddDays(1));
            VerifyTranslation<DateTimeOffset?>("datetime-tz", offsetMoment, offsetMoment.AddDays(1));
            VerifyTranslation<DateTimeOffset?>("timestamp_timezone", offsetMoment, offsetMoment.AddDays(1));
            VerifyTranslation<TimeSpan?>("time", TimeSpan.FromHours(1), TimeSpan.FromHours(2));
        }
    }
}
