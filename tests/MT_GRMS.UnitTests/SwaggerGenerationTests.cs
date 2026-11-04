using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Swashbuckle.AspNetCore.Swagger;
using Xunit;

namespace MT_GRMS.UnitTests;

public class SwaggerGenerationTests
{
    [Fact]
    public void Swagger_Generation_Should_Succeed_Without_Exceptions()
    {
        // GIVEN: Service collection với API Controllers và Swagger
        var services = new ServiceCollection();
        services.AddLogging();
        var mockEnv = new Mock<IWebHostEnvironment>();
        mockEnv.Setup(e => e.EnvironmentName).Returns("Development");
        mockEnv.Setup(e => e.ApplicationName).Returns("MT_GRMS.Api");
        services.AddSingleton<IWebHostEnvironment>(mockEnv.Object);
        services.AddSingleton<IHostEnvironment>(mockEnv.Object);

        services.AddControllers()
            .AddApplicationPart(typeof(MT_GRMS.Api.Controllers.MediaController).Assembly);
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        var serviceProvider = services.BuildServiceProvider();

        // WHEN: Tạo Swagger Document "v1"
        var swaggerProvider = serviceProvider.GetRequiredService<ISwaggerProvider>();
        var doc = swaggerProvider.GetSwagger("v1");

        // THEN: Không ném exception và document có endpoint /api/v1/media/upload
        Assert.NotNull(doc);
        Assert.True(doc.Paths.ContainsKey("/api/v1/media/upload"));
    }
}
