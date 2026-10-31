using EntityFrameworkCore.OpenEdge.Extensions;
using EntityFrameworkCore.OpenEdge.Scaffolding.Internal;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.OpenEdge.Design.Internal
{
    public class OpenEdgeDesignTimeServices : IDesignTimeServices
    {
        public void ConfigureDesignTimeServices(IServiceCollection serviceCollection)
        {
            serviceCollection.AddEntityFrameworkOpenEdge();

            new EntityFrameworkRelationalDesignServicesBuilder(serviceCollection)
                .TryAdd<IDatabaseModelFactory, OpenEdgeDatabaseModelFactory>()
                .TryAdd<IProviderConfigurationCodeGenerator, OpenEdgeCodeGenerator>()
                .TryAdd<IAnnotationCodeGenerator, OpenEdgeAnnotationCodeGenerator>()
                .TryAddCoreServices();
        }
    }
}
