using System;
using System.Collections.Generic;
using System.Linq;
using EFCore.OpenEdge.FunctionalTests.TestUtilities;
using EntityFrameworkCore.OpenEdge.Design.Internal;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace EFCore.OpenEdge.FunctionalTests.Integration
{
    /// <summary>
    /// Scaffolds a real OpenEdge database. The test only reads the database catalog.
    /// </summary>
    public class LiveScaffoldingTests
    {
        private readonly ITestOutputHelper _output;

        public LiveScaffoldingTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [LiveDatabaseFact]
        public void ScaffoldModel_Should_Generate_Compilable_Code_From_Real_Database()
        {
            var services = new ServiceCollection()
                .AddEntityFrameworkDesignTimeServices();
            new OpenEdgeDesignTimeServices().ConfigureDesignTimeServices(services);

            using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            using var scope = serviceProvider.CreateScope();
            var scaffolder = scope.ServiceProvider.GetRequiredService<IReverseEngineerScaffolder>();

            var scaffoldedModel = scaffolder.ScaffoldModel(
                LiveDatabaseFactAttribute.ConnectionString,
                new DatabaseModelFactoryOptions(),
                new ModelReverseEngineerOptions(),
                new ModelCodeGenerationOptions
                {
                    ContextName = "ScaffoldedContext",
                    ModelNamespace = "Scaffolded.Models",
                    ContextNamespace = "Scaffolded.Models",
                    RootNamespace = "Scaffolded",
                    Language = "C#",
                    SuppressOnConfiguring = true
                });

            _output.WriteLine($"Scaffolded {scaffoldedModel.AdditionalFiles.Count} entity types.");
            scaffoldedModel.AdditionalFiles.Should().NotBeEmpty();

            var sources = scaffoldedModel.AdditionalFiles
                .Select(file => file.Code)
                .Append(scaffoldedModel.ContextFile.Code);

            GetCompilationErrors(sources).Should().BeEmpty();
        }

        private static IEnumerable<string> GetCompilationErrors(IEnumerable<string> sources)
        {
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(System.IO.Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path));

            var compilation = CSharpCompilation.Create(
                "ScaffoldedModel",
                sources.Select(source => CSharpSyntaxTree.ParseText(source)),
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            return compilation.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.ToString());
        }
    }
}
