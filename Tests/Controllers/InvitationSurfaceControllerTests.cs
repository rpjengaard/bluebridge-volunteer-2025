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
using Web.ViewModels;

namespace Tests.Controllers;

public class InvitationSurfaceControllerTests
{
    private readonly Mock<IInvitationService> _invitationServiceMock;
    private readonly Mock<IUmbracoContextAccessor> _umbracoContextAccessorMock;
    private readonly Mock<IUmbracoDatabaseFactory> _databaseFactoryMock;
    private readonly Mock<ServiceContext> _serviceContextMock;
    private readonly Mock<AppCaches> _appCachesMock;
    private readonly Mock<IProfilingLogger> _profilingLoggerMock;
    private readonly Mock<IPublishedUrlProvider> _publishedUrlProviderMock;

    public InvitationSurfaceControllerTests()
    {
        _invitationServiceMock = new Mock<IInvitationService>();
        _umbracoContextAccessorMock = new Mock<IUmbracoContextAccessor>();
        _databaseFactoryMock = new Mock<IUmbracoDatabaseFactory>();
        _serviceContextMock = new Mock<ServiceContext>();
        _appCachesMock = new Mock<AppCaches>();
        _profilingLoggerMock = new Mock<IProfilingLogger>();
        _publishedUrlProviderMock = new Mock<IPublishedUrlProvider>();
    }

    private InvitationSurfaceController CreateController()
    {
        var controller = new InvitationSurfaceController(
            _umbracoContextAccessorMock.Object,
            _databaseFactoryMock.Object,
            _serviceContextMock.Object,
            _appCachesMock.Object,
            _profilingLoggerMock.Object,
            _publishedUrlProviderMock.Object,
            _invitationServiceMock.Object);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("example.com");

        var tempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        controller.TempData = tempData;
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        return controller;
    }

    #region AcceptInvitation (GET) Tests

    [Fact]
    public async Task AcceptInvitation_WithNullToken_ShouldReturnViewWithError()
    {
        // Arrange
        var controller = CreateController();

        // Act
        var result = await controller.AcceptInvitation(null!);

        // Assert
        result.Should().BeOfType<ViewResult>();
        controller.TempData["InvitationError"].Should().NotBeNull();
    }

    [Fact]
    public async Task AcceptInvitation_WithEmptyToken_ShouldReturnViewWithError()
    {
        // Arrange
        var controller = CreateController();

        // Act
        var result = await controller.AcceptInvitation("");

        // Assert
        result.Should().BeOfType<ViewResult>();
        controller.TempData["InvitationError"].Should().NotBeNull();
    }

    [Fact]
    public async Task AcceptInvitation_WithInvalidToken_ShouldReturnViewWithError()
    {
        // Arrange
        var controller = CreateController();
        _invitationServiceMock.Setup(x => x.GetMemberByTokenAsync("invalid-token"))
            .ReturnsAsync((MemberInvitationInfo?)null);

        // Act
        var result = await controller.AcceptInvitation("invalid-token");

        // Assert
        result.Should().BeOfType<ViewResult>();
        controller.TempData["InvitationError"].Should().NotBeNull();
    }

    [Fact]
    public async Task AcceptInvitation_WithValidToken_ShouldReturnViewWithModel()
    {
        // Arrange
        var controller = CreateController();
        var memberInfo = new MemberInvitationInfo
        {
            MemberId = 1,
            Email = "test@example.com",
            FirstName = "Test",
            LastName = "User",
            Birthdate = new DateTime(1990, 1, 1)
        };
        var crews = new List<CrewInfo>
        {
            new() { Id = 1, Name = "Crew 1" },
            new() { Id = 2, Name = "Crew 2" }
        };

        _invitationServiceMock.Setup(x => x.GetMemberByTokenAsync("valid-token"))
            .ReturnsAsync(memberInfo);
        _invitationServiceMock.Setup(x => x.GetAvailableCrewsAsync())
            .ReturnsAsync(crews);

        // Act
        var result = await controller.AcceptInvitation("valid-token");

        // Assert
        result.Should().BeOfType<ViewResult>();
        var viewResult = result as ViewResult;
        viewResult!.Model.Should().BeOfType<AcceptInvitationViewModel>();

        var model = viewResult.Model as AcceptInvitationViewModel;
        model!.Token.Should().Be("valid-token");
        model.MemberName.Should().Be("Test User");
        model.FirstName.Should().Be("Test");
        model.Email.Should().Be("test@example.com");
        model.AvailableCrews.Should().HaveCount(2);
    }

    #endregion

    #region HandleAcceptInvitation (POST) Tests

    [Fact]
    public async Task HandleAcceptInvitation_WithEmptyToken_ShouldRedirectWithError()
    {
        // Arrange
        var controller = CreateController();
        var model = new AcceptInvitationViewModel { Token = "" };

        // Act
        var result = await controller.HandleAcceptInvitation(model);

        // Assert
        result.Should().BeOfType<RedirectToActionResult>();
        controller.TempData["InvitationError"].Should().NotBeNull();
    }

    [Fact]
    public async Task HandleAcceptInvitation_WithNoSelectedCrews_ShouldRedirectWithError()
    {
        // Arrange
        var controller = CreateController();
        var model = new AcceptInvitationViewModel
        {
            Token = "valid-token",
            SelectedCrewIds = new List<int>(),
            Birthdate = DateTime.Now,
            Password = "Password123!"
        };

        // Act
        var result = await controller.HandleAcceptInvitation(model);

        // Assert
        result.Should().BeOfType<RedirectToActionResult>();
        controller.TempData["InvitationError"].Should().NotBeNull();
    }

    [Fact]
    public async Task HandleAcceptInvitation_WithNoBirthdate_ShouldRedirectWithError()
    {
        // Arrange
        var controller = CreateController();
        var model = new AcceptInvitationViewModel
        {
            Token = "valid-token",
            SelectedCrewIds = new List<int> { 1 },
            Birthdate = null,
            Password = "Password123!"
        };

        // Act
        var result = await controller.HandleAcceptInvitation(model);

        // Assert
        result.Should().BeOfType<RedirectToActionResult>();
        controller.TempData["InvitationError"].Should().NotBeNull();
    }

    [Fact]
    public async Task HandleAcceptInvitation_WithNoPassword_ShouldRedirectWithError()
    {
        // Arrange
        var controller = CreateController();
        var model = new AcceptInvitationViewModel
        {
            Token = "valid-token",
            SelectedCrewIds = new List<int> { 1 },
            Birthdate = DateTime.Now,
            Password = ""
        };

        // Act
        var result = await controller.HandleAcceptInvitation(model);

        // Assert
        result.Should().BeOfType<RedirectToActionResult>();
        controller.TempData["InvitationError"].Should().NotBeNull();
    }

    [Fact]
    public async Task HandleAcceptInvitation_WithMismatchedPasswords_ShouldRedirectWithError()
    {
        // Arrange
        var controller = CreateController();
        var model = new AcceptInvitationViewModel
        {
            Token = "valid-token",
            SelectedCrewIds = new List<int> { 1 },
            Birthdate = DateTime.Now,
            Password = "Password123!",
            ConfirmPassword = "DifferentPassword!"
        };

        // Act
        var result = await controller.HandleAcceptInvitation(model);

        // Assert
        result.Should().BeOfType<RedirectToActionResult>();
        controller.TempData["InvitationError"].Should().NotBeNull();
    }

    [Fact]
    public async Task HandleAcceptInvitation_WithShortPassword_ShouldRedirectWithError()
    {
        // Arrange
        var controller = CreateController();
        var model = new AcceptInvitationViewModel
        {
            Token = "valid-token",
            SelectedCrewIds = new List<int> { 1 },
            Birthdate = DateTime.Now,
            Password = "short",
            ConfirmPassword = "short"
        };

        // Act
        var result = await controller.HandleAcceptInvitation(model);

        // Assert
        result.Should().BeOfType<RedirectToActionResult>();
        controller.TempData["InvitationError"].Should().NotBeNull();
    }

    [Fact]
    public async Task HandleAcceptInvitation_WithFailedAcceptance_ShouldRedirectWithError()
    {
        // Arrange
        var controller = CreateController();
        var model = new AcceptInvitationViewModel
        {
            Token = "valid-token",
            SelectedCrewIds = new List<int> { 1 },
            Birthdate = new DateTime(1990, 1, 1),
            Password = "Password123!",
            ConfirmPassword = "Password123!"
        };

        _invitationServiceMock.Setup(x => x.AcceptInvitationAsync(
            model.Token,
            model.SelectedCrewIds,
            model.Birthdate.Value,
            model.Password,
            It.IsAny<string>()))
            .ReturnsAsync(new AcceptInvitationResult { Success = false, Message = "Error" });

        // Act
        var result = await controller.HandleAcceptInvitation(model);

        // Assert
        result.Should().BeOfType<RedirectToActionResult>();
        controller.TempData["InvitationError"].Should().Be("Error");
    }

    [Fact]
    public async Task HandleAcceptInvitation_WithSuccessfulAcceptance_ShouldRedirectToConfirmation()
    {
        // Arrange
        var controller = CreateController();
        var model = new AcceptInvitationViewModel
        {
            Token = "valid-token",
            SelectedCrewIds = new List<int> { 1, 2 },
            Birthdate = new DateTime(1990, 1, 1),
            Password = "Password123!",
            ConfirmPassword = "Password123!"
        };

        _invitationServiceMock.Setup(x => x.AcceptInvitationAsync(
            model.Token,
            model.SelectedCrewIds,
            model.Birthdate.Value,
            model.Password,
            It.IsAny<string>()))
            .ReturnsAsync(new AcceptInvitationResult
            {
                Success = true,
                MemberName = "Test User",
                SelectedCrewNames = new[] { "Crew 1", "Crew 2" }
            });

        // Act
        var result = await controller.HandleAcceptInvitation(model);

        // Assert
        result.Should().BeOfType<RedirectToActionResult>();
        var redirectResult = result as RedirectToActionResult;
        redirectResult!.ActionName.Should().Be("InvitationConfirmation");
        controller.TempData["MemberName"].Should().Be("Test User");
        controller.TempData["SelectedCrews"].Should().Be("Crew 1, Crew 2");
    }

    #endregion

    #region InvitationConfirmation Tests

    [Fact]
    public void InvitationConfirmation_ShouldReturnView()
    {
        // Arrange
        var controller = CreateController();

        // Act
        var result = controller.InvitationConfirmation();

        // Assert
        result.Should().BeOfType<ViewResult>();
    }

    #endregion
}
