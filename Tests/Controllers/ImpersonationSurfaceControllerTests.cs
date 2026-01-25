using Code.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Logging;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Infrastructure.Persistence;
using Web.Controllers;

namespace Tests.Controllers;

public class ImpersonationSurfaceControllerTests
{
    private readonly Mock<IMemberImpersonationService> _impersonationServiceMock;
    private readonly Mock<IUmbracoContextAccessor> _umbracoContextAccessorMock;
    private readonly Mock<IUmbracoDatabaseFactory> _databaseFactoryMock;
    private readonly Mock<ServiceContext> _serviceContextMock;
    private readonly Mock<AppCaches> _appCachesMock;
    private readonly Mock<IProfilingLogger> _profilingLoggerMock;
    private readonly Mock<IPublishedUrlProvider> _publishedUrlProviderMock;

    public ImpersonationSurfaceControllerTests()
    {
        _impersonationServiceMock = new Mock<IMemberImpersonationService>();
        _umbracoContextAccessorMock = new Mock<IUmbracoContextAccessor>();
        _databaseFactoryMock = new Mock<IUmbracoDatabaseFactory>();
        _serviceContextMock = new Mock<ServiceContext>();
        _appCachesMock = new Mock<AppCaches>();
        _profilingLoggerMock = new Mock<IProfilingLogger>();
        _publishedUrlProviderMock = new Mock<IPublishedUrlProvider>();
    }

    private ImpersonationSurfaceController CreateController()
    {
        var controller = new ImpersonationSurfaceController(
            _umbracoContextAccessorMock.Object,
            _databaseFactoryMock.Object,
            _serviceContextMock.Object,
            _appCachesMock.Object,
            _profilingLoggerMock.Object,
            _publishedUrlProviderMock.Object,
            _impersonationServiceMock.Object);

        var httpContext = new DefaultHttpContext();
        var tempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        controller.TempData = tempData;
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        return controller;
    }

    #region StopImpersonation Tests

    [Fact]
    public async Task StopImpersonation_ShouldCallServiceAndRedirect()
    {
        // Arrange
        var controller = CreateController();

        // Act
        var result = await controller.StopImpersonation();

        // Assert
        _impersonationServiceMock.Verify(x => x.StopImpersonationAsync(), Times.Once);
        result.Should().BeOfType<RedirectResult>();
        var redirectResult = result as RedirectResult;
        redirectResult!.Url.Should().Be("/umbraco");
    }

    [Fact]
    public async Task StopImpersonation_WhenServiceThrows_ShouldNotCatch()
    {
        // Arrange
        var controller = CreateController();
        _impersonationServiceMock.Setup(x => x.StopImpersonationAsync())
            .ThrowsAsync(new Exception("Stop failed"));

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(() => controller.StopImpersonation());
    }

    #endregion
}
