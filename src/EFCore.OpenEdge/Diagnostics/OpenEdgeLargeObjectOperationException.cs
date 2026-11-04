using System;

namespace EntityFrameworkCore.OpenEdge.Diagnostics
{
    /// <summary>
    /// Thrown when a query compares, sorts or pattern-matches a CLOB or BLOB value.
    /// OpenEdge rejects these operations, and comparing a large object with a parameter can crash
    /// the OpenEdge SQL server process. The provider therefore stops the query before it is sent.
    /// </summary>
    public class OpenEdgeLargeObjectOperationException : InvalidOperationException
    {
        /// <summary>
        /// Creates the exception for a large object used in an unsupported operation.
        /// </summary>
        /// <param name="columnName">The column name, or <see langword="null"/> when the value is not a column.</param>
        /// <param name="storeType">The store type of the value, for example "clob".</param>
        /// <param name="operation">The SQL operation the value was used in, for example "ORDER BY".</param>
        public OpenEdgeLargeObjectOperationException(string columnName, string storeType, string operation)
            : base(CreateMessage(columnName, storeType, operation))
        {
            ColumnName = columnName;
            StoreType = storeType;
            Operation = operation;
        }

        /// <summary>
        /// The column name, or <see langword="null"/> when the value is not a column.
        /// </summary>
        public string ColumnName { get; }

        /// <summary>
        /// The store type of the value, for example "clob".
        /// </summary>
        public string StoreType { get; }

        /// <summary>
        /// The SQL operation the value was used in, for example "ORDER BY".
        /// </summary>
        public string Operation { get; }

        private static string CreateMessage(string columnName, string storeType, string operation)
        {
            var subject = columnName != null
                ? $"the {storeType} column '{columnName}'"
                : $"a {storeType} value";

            return $"OpenEdge cannot use {subject} in {operation}. OpenEdge rejects this, and with a parameter "
                + "it can crash the OpenEdge SQL server, so the query was not sent. Filter or sort on another "
                + "column, or load the rows and work with the value in memory.";
        }
    }
}
