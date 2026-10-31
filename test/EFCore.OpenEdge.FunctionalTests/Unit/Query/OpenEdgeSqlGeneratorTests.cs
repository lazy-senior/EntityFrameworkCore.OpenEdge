using System;
using System.Data.Common;
using System.Linq;
using EFCore.OpenEdge.FunctionalTests.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace EFCore.OpenEdge.FunctionalTests.Unit.Query
{
    /// <summary>
    /// Verifies the commands generated for queries that ToQueryString cannot show, without a database.
    /// The connection is never opened and the command is captured right before it would execute.
    /// </summary>
    public class OpenEdgeSqlGeneratorTests
    {
        // EF Core clears the command when it fails, so the exception keeps a copy of its contents.
        private sealed class CommandCapturedException(DbCommand command) : Exception
        {
            public string CommandText { get; } = command.CommandText;

            public object[] ParameterValues { get; } = command.Parameters
                .Cast<DbParameter>()
                .Select(parameter => parameter.Value)
                .ToArray();
        }

        private sealed class SuppressConnectionInterceptor : DbConnectionInterceptor
        {
            public override InterceptionResult ConnectionOpening(
                DbConnection connection,
                ConnectionEventData eventData,
                InterceptionResult result)
                => InterceptionResult.Suppress();
        }

        private sealed class CaptureCommandInterceptor : DbCommandInterceptor
        {
            public override InterceptionResult<DbDataReader> ReaderExecuting(
                DbCommand command,
                CommandEventData eventData,
                InterceptionResult<DbDataReader> result)
                => throw new CommandCapturedException(command);
        }

        private static CommandCapturedException CaptureCommand(Action<ECommerceTestContext> executeQuery)
        {
            var options = new DbContextOptionsBuilder<ECommerceTestContext>()
                .UseOpenEdge("dsn=none", "PUB")
                .AddInterceptors(new SuppressConnectionInterceptor(), new CaptureCommandInterceptor())
                .Options;

            using var context = new ECommerceTestContext(options);

            return Assert.Throws<CommandCapturedException>(() => executeQuery(context));
        }

        [Fact]
        public void Count_Should_Cast_Result_To_Int()
        {
            var command = CaptureCommand(context => context.Customers.Count());

            command.CommandText.Should().Contain("CAST(COUNT(*) AS INT)");
        }

        [Fact]
        public void LongCount_Should_Not_Cast_Result()
        {
            var command = CaptureCommand(context => context.Customers.LongCount());

            command.CommandText.Should().Contain("COUNT(*)").And.NotContain("CAST");
        }

        [Fact]
        public void GroupBy_Count_Should_Cast_Result_To_Int()
        {
            var command = CaptureCommand(context => context.Customers
                .GroupBy(customer => customer.City)
                .Select(group => new { group.Key, Count = group.Count() })
                .ToList());

            command.CommandText.Should().Contain("CAST(COUNT(*) AS INT)");
        }

        [Fact]
        public void Any_Should_Move_Exists_Into_Single_Row_Subquery()
        {
            var command = CaptureCommand(context => context.Customers.Any());

            command.CommandText.Should()
                .Contain("CASE WHEN (SELECT 1 FROM pub.\"_File\"")
                .And.Contain("AND EXISTS (")
                .And.EndWith("WHERE \"oe_single_row\".\"_File-Name\" = '_File'");
        }

        [Fact]
        public void All_Should_Move_Negated_Exists_Into_Single_Row_Subquery()
        {
            var command = CaptureCommand(context => context.Customers.All(customer => customer.Age > 18));

            command.CommandText.Should()
                .Contain("CASE WHEN (SELECT 1 FROM pub.\"_File\"")
                .And.Contain("AND NOT EXISTS (");
        }

        [Fact]
        public void Where_Exists_Should_Stay_In_Where_Clause()
        {
            var command = CaptureCommand(context => context.Customers
                .Where(customer => customer.Orders.Any())
                .ToList());

            command.CommandText.Should()
                .Contain("WHERE EXISTS (")
                .And.NotContain("_File");
        }

        [Fact]
        public void Repeated_Parameter_Should_Bind_One_Value_Per_Placeholder()
        {
            var city = "Berlin";

            var command = CaptureCommand(context => context.Customers
                .Where(customer => customer.City == city || customer.Name == city)
                .ToList());

            command.CommandText.Count(character => character == '?').Should().Be(2);
            command.ParameterValues.Should().Equal(city, city);
        }

        [Fact]
        public void Parameters_Should_Bind_In_Placeholder_Order()
        {
            var city = "Berlin";
            var name = "Ada";

            var command = CaptureCommand(context => context.Customers
                .Where(customer => customer.City == city && customer.Name == name && customer.Email == city)
                .ToList());

            command.ParameterValues.Should().Equal(city, name, city);
        }
    }
}
