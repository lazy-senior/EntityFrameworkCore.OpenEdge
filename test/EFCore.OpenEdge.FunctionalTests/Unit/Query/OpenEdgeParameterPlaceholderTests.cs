using System;
using System.Linq;
using System.Threading.Tasks;
using EFCore.OpenEdge.FunctionalTests.Shared;
using EFCore.OpenEdge.FunctionalTests.Shared.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EFCore.OpenEdge.FunctionalTests.Unit.Query
{
    public class OpenEdgeParameterPlaceholderTests
    {
        private static ECommerceTestContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<ECommerceTestContext>()
                .UseOpenEdge("dsn=none", "PUB")
                .Options;

            return new ECommerceTestContext(options);
        }

        // Non-unique parameter names make query compilation loop forever, so fail after a timeout instead.
        private static Task<string> GenerateSqlAsync(IQueryable<Customer> query)
            => Task.Run(() => query.ToQueryString()).WaitAsync(TimeSpan.FromSeconds(30));

        [Fact]
        public async Task ToQueryString_Should_Use_Positional_Placeholders_For_Multiple_Parameters()
        {
            using var context = CreateContext();
            var minimumAge = 18;
            var city = "Berlin";

            var sql = await GenerateSqlAsync(
                context.Customers.Where(customer => customer.Age > minimumAge && customer.City == city));

            sql.Should().Contain("> ? AND").And.Contain("= ?");
        }

        [Fact]
        public async Task ToQueryString_Should_Inline_Skip_And_Take()
        {
            using var context = CreateContext();
            var skip = 10;
            var take = 20;

            var sql = await GenerateSqlAsync(
                context.Customers.OrderBy(customer => customer.Id).Skip(skip).Take(take));

            sql.Should().Contain("OFFSET 10 ROWS FETCH NEXT 20 ROWS ONLY");
        }

        [Fact]
        public async Task ToQueryString_Should_Use_Placeholders_For_Collection_Contains()
        {
            using var context = CreateContext();
            int[] ids = [1, 2, 3];

            var sql = await GenerateSqlAsync(
                context.Customers.Where(customer => ids.Contains(customer.Id)));

            sql.Should().Contain("IN (?, ?, ?)");
        }
    }
}
