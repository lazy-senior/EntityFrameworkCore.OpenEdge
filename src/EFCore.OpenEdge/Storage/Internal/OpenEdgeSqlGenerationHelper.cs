using System.Text;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.OpenEdge.Storage.Internal
{
    public class OpenEdgeSqlGenerationHelper : RelationalSqlGenerationHelper, IOpenEdgeSqlGenerationHelper
    {
        public OpenEdgeSqlGenerationHelper(RelationalSqlGenerationHelperDependencies dependencies)
            : base(dependencies)
        {
            
        }

        public override string StatementTerminator { get; } = "";

        // Only the placeholder in the command text is positional. Parameter names must stay unique,
        // because EF Core loops over GenerateParameterName until it finds an unused name.
        public override string GenerateParameterNamePlaceholder(string name)
        {
            return "?";
        }

        public override void GenerateParameterNamePlaceholder(StringBuilder builder, string name)
        {
            builder.Append('?');
        }

        public override void DelimitIdentifier(StringBuilder builder, string identifier)
        {
            // Row ID cannot be delimited in OpenEdge
            if (identifier == "rowid")
            {
                EscapeIdentifier(builder, identifier);
                return;
            }

            base.DelimitIdentifier(builder, identifier);
        }

        public override string DelimitIdentifier(string identifier)
        {
            // Row ID cannot be delimited in OpenEdge
            if (identifier == "rowid")
            {
                return EscapeIdentifier(identifier);
            }

            return base.DelimitIdentifier(identifier);
        }
    }
}