using System;
using System.Linq;
using EFCore.OpenEdge.FunctionalTests.TestUtilities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EFCore.OpenEdge.FunctionalTests.Integration
{
    /// <summary>
    /// Runs queries against a real OpenEdge database without writing to it. The tests only read the
    /// _File and _Field metaschema tables, which exist in every OpenEdge database, so they need no
    /// test tables, seed data or write permission.
    /// </summary>
    public class LiveReadOnlyQueryTests
    {
        private sealed class MetaschemaTable
        {
            public string Name { get; set; }
        }

        private sealed class MetaschemaField
        {
            public string Name { get; set; }
            public string DataType { get; set; }
            public int Order { get; set; }
            public bool IsMandatory { get; set; }
        }

        private sealed class MetaschemaContext : DbContext
        {
            public DbSet<MetaschemaTable> Tables { get; set; }
            public DbSet<MetaschemaField> Fields { get; set; }

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
                => optionsBuilder.UseOpenEdge(LiveDatabaseFactAttribute.ConnectionString, "pub");

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<MetaschemaTable>(entity =>
                {
                    entity.HasNoKey();
                    entity.ToTable("_File", "pub");
                    entity.Property(table => table.Name).HasColumnName("_File-Name");
                });

                modelBuilder.Entity<MetaschemaField>(entity =>
                {
                    entity.HasNoKey();
                    entity.ToTable("_Field", "pub");
                    entity.Property(field => field.Name).HasColumnName("_Field-Name");
                    entity.Property(field => field.DataType).HasColumnName("_Data-Type");
                    entity.Property(field => field.Order).HasColumnName("_Order");
                    entity.Property(field => field.IsMandatory).HasColumnName("_Mandatory");
                });
            }
        }

        [LiveDatabaseFact]
        public void Where_Should_Bind_Multiple_Parameters()
        {
            using var context = new MetaschemaContext();
            var firstName = "_File";
            var secondName = "_Field";

            var names = context.Tables
                .Where(table => table.Name == firstName || table.Name == secondName)
                .Select(table => table.Name)
                .ToList();

            names.Should().BeEquivalentTo(firstName, secondName);
        }

        [LiveDatabaseFact]
        public void Where_Should_Bind_Parameter_Used_More_Than_Once()
        {
            using var context = new MetaschemaContext();
            var name = "_File";

            var names = context.Tables
                .Where(table => table.Name == name || table.Name.StartsWith(name))
                .Select(table => table.Name)
                .ToList();

            names.Should().Contain(name);
        }

        [LiveDatabaseFact]
        public void Contains_Should_Bind_String_Collection()
        {
            using var context = new MetaschemaContext();
            string[] names = ["_File", "_Field"];

            var foundNames = context.Tables
                .Where(table => names.Contains(table.Name))
                .Select(table => table.Name)
                .ToList();

            foundNames.Should().BeEquivalentTo(names);
        }

        [LiveDatabaseFact]
        public void Contains_Should_Bind_Integer_Collection()
        {
            using var context = new MetaschemaContext();
            int[] orders = [10, 20, 30];
            var expectedCount = context.Fields
                .Select(field => field.Order)
                .AsEnumerable()
                .Count(order => orders.Contains(order));

            var count = context.Fields.Count(field => orders.Contains(field.Order));

            count.Should().Be(expectedCount);
        }

        [LiveDatabaseFact]
        public void Negated_Contains_Should_Exclude_Collection_Values()
        {
            using var context = new MetaschemaContext();
            string[] names = ["_File", "_Field"];
            var totalCount = context.Tables.Count();

            var count = context.Tables.Count(table => !names.Contains(table.Name));

            count.Should().Be(totalCount - names.Length);
        }

        [LiveDatabaseFact]
        public void Skip_And_Take_Should_Return_Requested_Page()
        {
            using var context = new MetaschemaContext();
            var skip = 2;
            var take = 3;
            var orderedNames = context.Tables
                .OrderBy(table => table.Name)
                .Select(table => table.Name)
                .ToList();

            var page = context.Tables
                .OrderBy(table => table.Name)
                .Select(table => table.Name)
                .Skip(skip)
                .Take(take)
                .ToList();

            page.Should().Equal(orderedNames.Skip(skip).Take(take));
        }

        [LiveDatabaseFact]
        public void Take_Should_Not_Reuse_Cached_Limit()
        {
            using var context = new MetaschemaContext();

            foreach (var take in new[] { 1, 2 })
            {
                var names = context.Tables
                    .OrderBy(table => table.Name)
                    .Select(table => table.Name)
                    .Take(take)
                    .ToList();

                names.Should().HaveCount(take);
            }
        }

        [LiveDatabaseFact]
        public void Boolean_Parameter_Should_Split_Rows_Without_Overlap()
        {
            using var context = new MetaschemaContext();
            var isMandatory = true;
            var isOptional = false;
            var totalCount = context.Fields.Count();

            var mandatoryCount = context.Fields.Count(field => field.IsMandatory == isMandatory);
            var optionalCount = context.Fields.Count(field => field.IsMandatory == isOptional);

            (mandatoryCount + optionalCount).Should().Be(totalCount);
        }

        [LiveDatabaseFact]
        public void StartsWith_Should_Bind_String_Parameter()
        {
            using var context = new MetaschemaContext();
            var prefix = "_Fi";

            var names = context.Tables
                .Where(table => table.Name.StartsWith(prefix))
                .Select(table => table.Name)
                .ToList();

            names.Should().Contain("_File").And.Contain("_Field");
        }

        [LiveDatabaseFact]
        public void GroupBy_Should_Count_All_Rows()
        {
            using var context = new MetaschemaContext();
            var totalCount = context.Fields.Count();

            var groups = context.Fields
                .GroupBy(field => field.DataType)
                .Select(group => new { group.Key, Count = group.Count() })
                .ToList();

            groups.Sum(group => group.Count).Should().Be(totalCount);
        }

        [LiveDatabaseFact]
        public void LongCount_Should_Match_Count()
        {
            using var context = new MetaschemaContext();

            var longCount = context.Tables.LongCount();

            longCount.Should().Be(context.Tables.Count());
        }

        [LiveDatabaseFact]
        public void All_Should_Evaluate_Every_Row()
        {
            using var context = new MetaschemaContext();
            var missingName = "_NoSuchTable";

            var allDiffer = context.Tables.All(table => table.Name != missingName);

            allDiffer.Should().BeTrue();
        }

        [LiveDatabaseFact]
        public void Any_Should_Return_False_Without_Match()
        {
            using var context = new MetaschemaContext();
            var missingName = "_NoSuchTable";

            var exists = context.Tables.Any(table => table.Name == missingName);

            exists.Should().BeFalse();
        }

        [LiveDatabaseFact]
        public void Any_Should_Find_Existing_Row()
        {
            using var context = new MetaschemaContext();
            var name = "_File";

            var exists = context.Tables.Any(table => table.Name == name);

            exists.Should().BeTrue();
        }
    }
}
