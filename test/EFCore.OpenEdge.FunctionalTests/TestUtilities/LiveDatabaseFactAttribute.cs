using System;
using Xunit;

namespace EFCore.OpenEdge.FunctionalTests.TestUtilities
{
    /// <summary>
    /// Marks a read-only test that runs against a real OpenEdge database. Opt in by setting the environment
    /// variable named by <see cref="ConnectionStringVariable"/>, for example to "DSN=MyDb". These tests do not
    /// need the appsettings.json used by the other database tests.
    /// </summary>
    public sealed class LiveDatabaseFactAttribute : FactAttribute
    {
        public const string ConnectionStringVariable = "OPENEDGE_SCAFFOLD_CONNECTION";

        public LiveDatabaseFactAttribute()
        {
            if (string.IsNullOrEmpty(ConnectionString))
            {
                Skip = $"Set {ConnectionStringVariable} to run against a real OpenEdge database.";
            }
        }

        public static string ConnectionString => Environment.GetEnvironmentVariable(ConnectionStringVariable);
    }
}
