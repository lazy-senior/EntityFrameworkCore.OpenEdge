using System;
using EntityFrameworkCore.OpenEdge.Design.Internal;
using EntityFrameworkCore.OpenEdge.Scaffolding.Internal;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Xunit;

namespace EFCore.OpenEdge.FunctionalTests.Unit.Design
{
    public class OpenEdgeDesignTimeServicesTests
    {
        // Builds the container the same way the EF Core tools do for `dotnet ef dbcontext scaffold`.
        private static ServiceProvider BuildDesignTimeServiceProvider(Action<IServiceCollection> configureServices = null)
        {
            var services = new ServiceCollection()
                .AddEntityFrameworkDesignTimeServices();

            new OpenEdgeDesignTimeServices().ConfigureDesignTimeServices(services);
            configureServices?.Invoke(services);

            return services.BuildServiceProvider(validateScopes: true);
        }

        private static DatabaseModel CreateCustomerDatabaseModel()
        {
            var databaseModel = new DatabaseModel { DefaultSchema = "pub" };
            var table = new DatabaseTable { Database = databaseModel, Name = "customer", Schema = "pub" };
            var column = new DatabaseColumn { Table = table, Name = "id", StoreType = "integer", IsNullable = false };

            table.Columns.Add(column);
            table.PrimaryKey = new DatabasePrimaryKey { Table = table, Name = "pk_customer", Columns = { column } };
            databaseModel.Tables.Add(table);

            return databaseModel;
        }

        [Fact]
        public void ConfigureDesignTimeServices_Should_Resolve_Database_Model_Factory()
        {
            using var serviceProvider = BuildDesignTimeServiceProvider();
            using var scope = serviceProvider.CreateScope();

            var modelFactory = scope.ServiceProvider.GetRequiredService<IDatabaseModelFactory>();

            modelFactory.Should().BeOfType<OpenEdgeDatabaseModelFactory>();
        }

        [Fact]
        public void ConfigureDesignTimeServices_Should_Resolve_Reverse_Engineer_Scaffolder()
        {
            using var serviceProvider = BuildDesignTimeServiceProvider();
            using var scope = serviceProvider.CreateScope();

            var scaffolder = scope.ServiceProvider.GetRequiredService<IReverseEngineerScaffolder>();

            scaffolder.Should().NotBeNull();
        }

        [Fact]
        public void ConfigureDesignTimeServices_Should_Resolve_Annotation_Code_Generator()
        {
            using var serviceProvider = BuildDesignTimeServiceProvider();
            using var scope = serviceProvider.CreateScope();

            var annotationCodeGenerator = scope.ServiceProvider.GetRequiredService<IAnnotationCodeGenerator>();

            annotationCodeGenerator.Should().BeOfType<OpenEdgeAnnotationCodeGenerator>();
        }

        [Fact]
        public void GenerateUseProvider_Should_Generate_UseOpenEdge_Call()
        {
            using var serviceProvider = BuildDesignTimeServiceProvider();
            using var scope = serviceProvider.CreateScope();
            var codeGenerator = scope.ServiceProvider.GetRequiredService<IProviderConfigurationCodeGenerator>();

            var useProvider = codeGenerator.GenerateUseProvider("dsn=MyDb");

            useProvider.Method.Should().Be("UseOpenEdge");
            useProvider.Arguments.Should().Contain("dsn=MyDb");
        }

        [Fact]
        public void ScaffoldModel_Should_Generate_DbContext_And_Entity_Type()
        {
            var modelFactory = new Mock<IDatabaseModelFactory>();
            modelFactory
                .Setup(f => f.Create(It.IsAny<string>(), It.IsAny<DatabaseModelFactoryOptions>()))
                .Returns(CreateCustomerDatabaseModel());

            using var serviceProvider = BuildDesignTimeServiceProvider(
                services => services.Replace(ServiceDescriptor.Singleton(modelFactory.Object)));
            using var scope = serviceProvider.CreateScope();
            var scaffolder = scope.ServiceProvider.GetRequiredService<IReverseEngineerScaffolder>();

            var scaffoldedModel = scaffolder.ScaffoldModel(
                "dsn=MyDb",
                new DatabaseModelFactoryOptions(),
                new ModelReverseEngineerOptions(),
                new ModelCodeGenerationOptions
                {
                    ContextName = "ShopContext",
                    ModelNamespace = "Shop.Models",
                    ContextNamespace = "Shop.Models",
                    RootNamespace = "Shop",
                    Language = "C#",
                    UseDataAnnotations = false
                });

            scaffoldedModel.ContextFile.Code.Should().Contain("optionsBuilder.UseOpenEdge(\"dsn=MyDb\"");
            scaffoldedModel.AdditionalFiles.Should().ContainSingle()
                .Which.Code.Should().Contain("public int Id { get; set; }");
        }
    }
}
