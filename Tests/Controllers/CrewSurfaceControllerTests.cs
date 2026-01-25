using Code.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Logging;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Infrastructure.Persistence;
using Web.Controllers;

namespace Tests.Controllers;

public class CrewSurfaceControllerTests
{
    private readonly Mock<IContentService> _contentServiceMock;
    private readonly Mock<IContentPublishingService> _contentPublishingServiceMock;
    private readonly Mock<IMemberManager> _memberManagerMock;
    private readonly Mock<IMemberService> _memberServiceMock;
    private readonly Mock<ICrewService> _crewServiceMock;
    private readonly Mock<AppCaches> _appCachesMock;
    private readonly Mock<IUmbracoContextAccessor> _umbracoContextAccessorMock;
    private readonly Mock<IUmbracoDatabaseFactory> _databaseFactoryMock;
    private readonly Mock<ServiceContext> _serviceContextMock;
    private readonly Mock<IProfilingLogger> _profilingLoggerMock;
    private readonly Mock<IPublishedUrlProvider> _publishedUrlProviderMock;

    public CrewSurfaceControllerTests()
    {
        _contentServiceMock = new Mock<IContentService>();
        _contentPublishingServiceMock = new Mock<IContentPublishingService>();
        _memberManagerMock = new Mock<IMemberManager>();
        _memberServiceMock = new Mock<IMemberService>();
        _crewServiceMock = new Mock<ICrewService>();
        _appCachesMock = new Mock<AppCaches>();
        _umbracoContextAccessorMock = new Mock<IUmbracoContextAccessor>();
        _databaseFactoryMock = new Mock<IUmbracoDatabaseFactory>();
        _serviceContextMock = new Mock<ServiceContext>();
        _profilingLoggerMock = new Mock<IProfilingLogger>();
        _publishedUrlProviderMock = new Mock<IPublishedUrlProvider>();
    }

    private CrewSurfaceController CreateController()
    {
        var controller = new CrewSurfaceController(
            _umbracoContextAccessorMock.Object,
            _databaseFactoryMock.Object,
            _serviceContextMock.Object,
            _appCachesMock.Object,
            _profilingLoggerMock.Object,
            _publishedUrlProviderMock.Object,
            _contentServiceMock.Object,
            _contentPublishingServiceMock.Object,
            _memberManagerMock.Object,
            _memberServiceMock.Object,
            _crewServiceMock.Object);

        var httpContext = new DefaultHttpContext();
        var tempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        controller.TempData = tempData;
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        return controller;
    }

    #region UpdateCrewDetails Tests

    [Fact]
    public async Task UpdateCrewDetails_WhenNotLoggedIn_ShouldRedirectWithError()
    {
        // Arrange
        var controller = CreateController();
        _memberManagerMock.Setup(x => x.GetCurrentMemberAsync())
            .ReturnsAsync((MemberIdentityUser?)null);

        // Act
        var result = await controller.UpdateCrewDetails(1, 18, "Description", "/crews/1");

        // Assert
        result.Should().BeOfType<RedirectResult>();
        controller.TempData["CrewError"].Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateCrewDetails_WhenVolunteer_ShouldRedirectWithError()
    {
        // Arrange
        var controller = CreateController();
        var currentUserMock = new Mock<MemberIdentityUser>();
        currentUserMock.Setup(x => x.Email).Returns("volunteer@example.com");

        _memberManagerMock.Setup(x => x.GetCurrentMemberAsync())
            .ReturnsAsync(currentUserMock.Object);
        _crewServiceMock.Setup(x => x.GetMemberCrewViewModeAsync("volunteer@example.com", 1))
            .ReturnsAsync(CrewViewMode.Volunteer);

        // Act
        var result = await controller.UpdateCrewDetails(1, 18, "Description", "/crews/1");

        // Assert
        result.Should().BeOfType<RedirectResult>();
        controller.TempData["CrewError"].Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateCrewDetails_WhenContentNotFound_ShouldRedirectWithError()
    {
        // Arrange
        var controller = CreateController();
        var currentUserMock = new Mock<MemberIdentityUser>();
        currentUserMock.Setup(x => x.Email).Returns("admin@example.com");

        _memberManagerMock.Setup(x => x.GetCurrentMemberAsync())
            .ReturnsAsync(currentUserMock.Object);
        _crewServiceMock.Setup(x => x.GetMemberCrewViewModeAsync("admin@example.com", 999))
            .ReturnsAsync(CrewViewMode.Admin);
        _contentServiceMock.Setup(x => x.GetById(999))
            .Returns((IContent?)null);

        // Act
        var result = await controller.UpdateCrewDetails(999, 18, "Description", "/crews/999");

        // Assert
        result.Should().BeOfType<RedirectResult>();
        controller.TempData["CrewError"].Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateCrewDetails_WithValidPermissions_ShouldUpdateAndRedirect()
    {
        // Arrange
        var controller = CreateController();
        var currentUserMock = new Mock<MemberIdentityUser>();
        currentUserMock.Setup(x => x.Email).Returns("scheduler@example.com");

        var contentMock = new Mock<IContent>();
        var publishResultMock = new Mock<PublishResult>();

        _memberManagerMock.Setup(x => x.GetCurrentMemberAsync())
            .ReturnsAsync(currentUserMock.Object);
        _crewServiceMock.Setup(x => x.GetMemberCrewViewModeAsync("scheduler@example.com", 1))
            .ReturnsAsync(CrewViewMode.Scheduler);
        _contentServiceMock.Setup(x => x.GetById(1))
            .Returns(contentMock.Object);
        _contentServiceMock.Setup(x => x.Publish(It.IsAny<IContent>(), It.IsAny<string[]>(), It.IsAny<int>()))
            .Returns(new PublishResult(PublishResultType.SuccessPublish, null, contentMock.Object));

        // Act
        var result = await controller.UpdateCrewDetails(1, 18, "New description", "/crews/1");

        // Assert
        result.Should().BeOfType<RedirectResult>();
        contentMock.Verify(x => x.SetValue("ageLimit", 18), Times.Once);
        _contentServiceMock.Verify(x => x.Save(contentMock.Object), Times.Once);
    }

    [Fact]
    public async Task UpdateCrewDetails_WithNullDescription_ShouldSetNullDescription()
    {
        // Arrange
        var controller = CreateController();
        var currentUserMock = new Mock<MemberIdentityUser>();
        currentUserMock.Setup(x => x.Email).Returns("admin@example.com");

        var contentMock = new Mock<IContent>();

        _memberManagerMock.Setup(x => x.GetCurrentMemberAsync())
            .ReturnsAsync(currentUserMock.Object);
        _crewServiceMock.Setup(x => x.GetMemberCrewViewModeAsync("admin@example.com", 1))
            .ReturnsAsync(CrewViewMode.Admin);
        _contentServiceMock.Setup(x => x.GetById(1))
            .Returns(contentMock.Object);
        _contentServiceMock.Setup(x => x.Publish(It.IsAny<IContent>(), It.IsAny<string[]>(), It.IsAny<int>()))
            .Returns(new PublishResult(PublishResultType.SuccessPublish, null, contentMock.Object));

        // Act
        var result = await controller.UpdateCrewDetails(1, 18, null, "/crews/1");

        // Assert
        contentMock.Verify(x => x.SetValue("description", (object?)null), Times.Once);
    }

    #endregion

    #region AcceptMember Tests

    [Fact]
    public async Task AcceptMember_WhenNotLoggedIn_ShouldRedirectWithError()
    {
        // Arrange
        var controller = CreateController();
        _memberManagerMock.Setup(x => x.GetCurrentMemberAsync())
            .ReturnsAsync((MemberIdentityUser?)null);

        // Act
        var result = await controller.AcceptMember(1, 100, "/crews/1");

        // Assert
        result.Should().BeOfType<RedirectResult>();
        controller.TempData["CrewError"].Should().NotBeNull();
    }

    [Fact]
    public async Task AcceptMember_WhenVolunteer_ShouldRedirectWithError()
    {
        // Arrange
        var controller = CreateController();
        var currentUserMock = new Mock<MemberIdentityUser>();
        currentUserMock.Setup(x => x.Email).Returns("volunteer@example.com");

        _memberManagerMock.Setup(x => x.GetCurrentMemberAsync())
            .ReturnsAsync(currentUserMock.Object);
        _crewServiceMock.Setup(x => x.GetMemberCrewViewModeAsync("volunteer@example.com", 1))
            .ReturnsAsync(CrewViewMode.Volunteer);

        // Act
        var result = await controller.AcceptMember(1, 100, "/crews/1");

        // Assert
        result.Should().BeOfType<RedirectResult>();
        controller.TempData["CrewError"].Should().NotBeNull();
    }

    [Fact]
    public async Task AcceptMember_WhenCrewNotFound_ShouldRedirectWithError()
    {
        // Arrange
        var controller = CreateController();
        var currentUserMock = new Mock<MemberIdentityUser>();
        currentUserMock.Setup(x => x.Email).Returns("scheduler@example.com");

        _memberManagerMock.Setup(x => x.GetCurrentMemberAsync())
            .ReturnsAsync(currentUserMock.Object);
        _crewServiceMock.Setup(x => x.GetMemberCrewViewModeAsync("scheduler@example.com", 999))
            .ReturnsAsync(CrewViewMode.Scheduler);
        _contentServiceMock.Setup(x => x.GetById(999))
            .Returns((IContent?)null);

        // Act
        var result = await controller.AcceptMember(999, 100, "/crews/999");

        // Assert
        result.Should().BeOfType<RedirectResult>();
        controller.TempData["CrewError"].Should().NotBeNull();
    }

    [Fact]
    public async Task AcceptMember_WhenMemberNotFound_ShouldRedirectWithError()
    {
        // Arrange
        var controller = CreateController();
        var currentUserMock = new Mock<MemberIdentityUser>();
        currentUserMock.Setup(x => x.Email).Returns("scheduler@example.com");

        var crewContentMock = new Mock<IContent>();
        crewContentMock.Setup(x => x.Key).Returns(Guid.NewGuid());

        _memberManagerMock.Setup(x => x.GetCurrentMemberAsync())
            .ReturnsAsync(currentUserMock.Object);
        _crewServiceMock.Setup(x => x.GetMemberCrewViewModeAsync("scheduler@example.com", 1))
            .ReturnsAsync(CrewViewMode.Scheduler);
        _contentServiceMock.Setup(x => x.GetById(1))
            .Returns(crewContentMock.Object);
        _memberServiceMock.Setup(x => x.GetById(999))
            .Returns((IMember?)null);

        // Act
        var result = await controller.AcceptMember(1, 999, "/crews/1");

        // Assert
        result.Should().BeOfType<RedirectResult>();
        controller.TempData["CrewError"].Should().NotBeNull();
    }

    [Fact]
    public async Task AcceptMember_WhenMemberAlreadyAssigned_ShouldRedirectWithError()
    {
        // Arrange
        var controller = CreateController();
        var currentUserMock = new Mock<MemberIdentityUser>();
        currentUserMock.Setup(x => x.Email).Returns("scheduler@example.com");

        var crewKey = Guid.NewGuid();
        var crewContentMock = new Mock<IContent>();
        crewContentMock.Setup(x => x.Key).Returns(crewKey);

        var memberMock = new Mock<IMember>();
        memberMock.Setup(x => x.GetValue<string>("crews"))
            .Returns($"umb://document/{crewKey:N}");

        _memberManagerMock.Setup(x => x.GetCurrentMemberAsync())
            .ReturnsAsync(currentUserMock.Object);
        _crewServiceMock.Setup(x => x.GetMemberCrewViewModeAsync("scheduler@example.com", 1))
            .ReturnsAsync(CrewViewMode.Scheduler);
        _contentServiceMock.Setup(x => x.GetById(1))
            .Returns(crewContentMock.Object);
        _memberServiceMock.Setup(x => x.GetById(100))
            .Returns(memberMock.Object);

        // Act
        var result = await controller.AcceptMember(1, 100, "/crews/1");

        // Assert
        result.Should().BeOfType<RedirectResult>();
        controller.TempData["CrewError"].Should().NotBeNull();
    }

    [Fact]
    public async Task AcceptMember_WithValidData_ShouldAssignAndRedirect()
    {
        // Arrange
        var controller = CreateController();
        var currentUserMock = new Mock<MemberIdentityUser>();
        currentUserMock.Setup(x => x.Email).Returns("scheduler@example.com");

        var crewKey = Guid.NewGuid();
        var crewContentMock = new Mock<IContent>();
        crewContentMock.Setup(x => x.Key).Returns(crewKey);

        var memberMock = new Mock<IMember>();
        memberMock.Setup(x => x.Name).Returns("Test User");
        memberMock.Setup(x => x.GetValue<string>("crews"))
            .Returns("");

        _memberManagerMock.Setup(x => x.GetCurrentMemberAsync())
            .ReturnsAsync(currentUserMock.Object);
        _crewServiceMock.Setup(x => x.GetMemberCrewViewModeAsync("scheduler@example.com", 1))
            .ReturnsAsync(CrewViewMode.Scheduler);
        _contentServiceMock.Setup(x => x.GetById(1))
            .Returns(crewContentMock.Object);
        _memberServiceMock.Setup(x => x.GetById(100))
            .Returns(memberMock.Object);

        // Act
        var result = await controller.AcceptMember(1, 100, "/crews/1");

        // Assert
        result.Should().BeOfType<RedirectResult>();
        controller.TempData["CrewSuccess"].Should().NotBeNull();
        memberMock.Verify(x => x.SetValue("crews", It.IsAny<string>()), Times.Once);
        _memberServiceMock.Verify(x => x.Save(memberMock.Object), Times.Once);
    }

    #endregion
}
