using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace EFCore.OpenEdge.FunctionalTests.TestUtilities
{
    /// <summary>
    /// A single column of a real table, as described by the database catalog.
    /// </summary>
    public sealed record ColumnTarget(string Schema, string Table, string Column, string StoreType, int Extent);

    public sealed class ProbeRow<T>
    {
        public T Value { get; set; }
    }

    public interface IColumnProbeContext
    {
        ColumnTarget Target { get; }
    }

    /// <summary>
    /// A context that maps exactly one column of an existing table, which lets the same queries run
    /// against any column without a scaffolded model. <typeparamref name="T"/> is the CLR type the
    /// provider maps the column to, and it must be nullable because the column may contain nulls.
    /// </summary>
    public sealed class ColumnProbeContext<T> : DbContext, IColumnProbeContext
    {
        public ColumnProbeContext(ColumnTarget target, string connectionString)
            : base(CreateOptions(connectionString))
        {
            Target = target;
        }

        public ColumnTarget Target { get; }

        public IQueryable<ProbeRow<T>> Rows => Set<ProbeRow<T>>();

        private static DbContextOptions CreateOptions(string connectionString)
            => new DbContextOptionsBuilder()
                .UseOpenEdge(connectionString, "pub")
                .ReplaceService<IModelCacheKeyFactory, ColumnProbeModelCacheKeyFactory>()
                .Options;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ProbeRow<T>>(entity =>
            {
                entity.HasNoKey();
                entity.ToTable(Target.Table, Target.Schema);
                entity.Property(row => row.Value)
                    .HasColumnName(Target.Column)
                    .HasColumnType(Target.StoreType);
            });
        }
    }

    /// <summary>
    /// Keeps one model per column, because EF Core otherwise caches a single model per context type.
    /// </summary>
    public sealed class ColumnProbeModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => context is IColumnProbeContext probeContext
                ? (context.GetType(), probeContext.Target, designTime)
                : (context.GetType(), designTime);
    }

    /// <summary>
    /// The queries a column is probed with. Every query is limited, because the probed tables can be huge.
    /// </summary>
    public static class ColumnProbeQueries<T>
    {
        // Reading a member of a constant makes EF Core send the value as a parameter.
        private sealed class ValueHolder
        {
            public T Value { get; init; }
        }

        public static IQueryable<ProbeRow<T>> Read(IQueryable<ProbeRow<T>> rows, int count)
            => rows.Take(count);

        public static IQueryable<T> Project(IQueryable<ProbeRow<T>> rows, int count)
            => rows.Select(row => row.Value).Take(count);

        public static IQueryable<ProbeRow<T>> IsNull(IQueryable<ProbeRow<T>> rows)
            => rows.Where(CreateNullComparison(isNull: true)).Take(1);

        public static IQueryable<ProbeRow<T>> IsNotNull(IQueryable<ProbeRow<T>> rows, int count)
            => rows.Where(CreateNullComparison(isNull: false)).Take(count);

        public static IQueryable<ProbeRow<T>> EqualToParameter(IQueryable<ProbeRow<T>> rows, T value)
            => rows.Where(CreateEquality(ReadHolder(value))).Take(1);

        public static IQueryable<ProbeRow<T>> EqualToLiteral(IQueryable<ProbeRow<T>> rows, T value)
        {
            var constantMethod = typeof(EF).GetMethod(nameof(EF.Constant))!.MakeGenericMethod(typeof(T));

            return rows.Where(CreateEquality(Expression.Call(constantMethod, ReadHolder(value)))).Take(1);
        }

        public static IQueryable<ProbeRow<T>> In(IQueryable<ProbeRow<T>> rows, List<T> values)
            => rows.Where(row => values.Contains(row.Value)).Take(values.Count);

        public static IQueryable<ProbeRow<T>> Ordered(IQueryable<ProbeRow<T>> rows, int count)
            => rows.OrderBy(row => row.Value).Take(count);

        private static MemberExpression ReadHolder(T value)
            => Expression.Property(Expression.Constant(new ValueHolder { Value = value }), nameof(ValueHolder.Value));

        private static Expression<Func<ProbeRow<T>, bool>> CreateNullComparison(bool isNull)
        {
            var row = Expression.Parameter(typeof(ProbeRow<T>), "row");
            var value = Expression.Property(row, nameof(ProbeRow<T>.Value));
            var nullValue = Expression.Constant(null, typeof(T));

            return Expression.Lambda<Func<ProbeRow<T>, bool>>(
                isNull ? Expression.Equal(value, nullValue) : Expression.NotEqual(value, nullValue),
                row);
        }

        private static Expression<Func<ProbeRow<T>, bool>> CreateEquality(Expression comparedValue)
        {
            var row = Expression.Parameter(typeof(ProbeRow<T>), "row");
            var value = Expression.Property(row, nameof(ProbeRow<T>.Value));

            return Expression.Lambda<Func<ProbeRow<T>, bool>>(Expression.Equal(value, comparedValue), row);
        }
    }
}
