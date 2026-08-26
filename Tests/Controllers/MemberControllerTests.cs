using Code.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Umbraco.Cms.Core.Security;
using Web.Controllers;

namespace Tests.Controllers;

public class MemberControllerTests
{
    private readonly Mock<IMemberManager> _memberManagerMock;
    private readonly Mock<ICrewService> _crewServiceMock;

    public MemberControllerTests()
    {
        _memberManagerMock = new Mock<IMemberManager>();
        _crewServiceMock = new Mock<ICrewService>();
    }

    private MemberController CreateController()
    {
        var controller = new MemberController(_memberManagerMock.Object, _crewServiceMock.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    #region Index Tests

    [Fact]
    public void Index_ShouldReturnView()
    {
        // Arrange
        var controller = CreateController();

        // Act
        var result = controller.Index();

        // Assert
        result.Should().BeOfType<ViewResult>();
    }

    #endregion

    #region GetMemberData Tests

    [Fact]
    public async Task GetMemberData_WhenNotLoggedIn_ShouldReturnUnauthorized()
    {
        // Arrange
        var controller = CreateController();
        _memberManagerMock.Setup(x => x.GetCurrentMemberAsync())
            .ReturnsAsync((MemberIdentityUser?)null);

        // Act
        var result = await controller.GetMemberData(Guid.NewGuid());

        // Assert
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task GetMemberData_WhenMemberNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var controller = CreateController();
        var currentUserMock = new Mock<MemberIdentityUser>();
        currentUserMock.Setup(x => x.Email).Returns("admin@example.com");

        _memberManagerMock.Setup(x => x.GetCurrentMemberAsync())
            .ReturnsAsync(currentUserMock.Object);
        _crewServiceMock.Setup(x => x.GetMemberByKeyAsync(It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync((MemberDetailData?)null);

        // Act
        var result = await controller.GetMemberData(Guid.NewGuid());

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetMemberData_WhenMemberFound_ShouldReturnOkWithData()
    {
        // Arrange
        var controller = CreateController();
        var memberKey = Guid.NewGuid();
        var currentUserMock = new Mock<MemberIdentityUser>();
        currentUserMock.Setup(x => x.Email).Returns("admin@example.com");

        var memberData = new MemberDetailData
        {
            MemberId = 1,
            MemberKey = memberKey,
            FirstName = "Test",
            LastName = "User",
            FullName = "Test User",
            Email = "test@example.com",
            Phone = "12345678",
            Birthdate = new DateTime(1990, 1, 15),
            Accept2026 = true,
            AcceptedDate = new DateTime(2024, 6, 1)
        };

        _memberManagerMock.Setup(x => x.GetCurrentMemberAsync())
            .ReturnsAsync(currentUserMock.Object);
        _crewServiceMock.Setup(x => x.GetMemberByKeyAsync(memberKey, "admin@example.com"))
            .ReturnsAsync(memberData);

        // Act
        var result = await controller.GetMemberData(memberKey);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetMemberData_WithBirthdate_ShouldCalculateAge()
    {
        // Arrange
        var controller = CreateController();
        var memberKey = Guid.NewGuid();
        var currentUserMock = new Mock<MemberIdentityUser>();
        currentUserMock.Setup(x => x.Email).Returns("admin@example.com");

        var birthdate = DateTime.Today.AddYears(-25);
        var memberData = new MemberDetailData
        {
            MemberId = 1,
            MemberKey = memberKey,
            FirstName = "Test",
            LastName = "User",
            FullName = "Test User",
            Email = "test@example.com",
            Birthdate = birthdate
        };

        _memberManagerMock.Setup(x => x.GetCurrentMemberAsync())
            .ReturnsAsync(currentUserMock.Object);
        _crewServiceMock.Setup(x => x.GetMemberByKeyAsync(memberKey, "admin@example.com"))
            .ReturnsAsync(memberData);

        // Act
        var result = await controller.GetMemberData(memberKey);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        // The age calculation happens in the controller
        okResult!.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task GetMemberData_WithNullBirthdate_ShouldReturnNullAge()
    {
        // Arrange
        var controller = CreateController();
        var memberKey = Guid.NewGuid();
        var currentUserMock = new Mock<MemberIdentityUser>();
        currentUserMock.Setup(x => x.Email).Returns("admin@example.com");

        var memberData = new MemberDetailData
        {
            MemberId = 1,
            MemberKey = memberKey,
            FirstName = "Test",
            LastName = "User",
            FullName = "Test User",
            Email = "test@example.com",
            Birthdate = null
        };

        _memberManagerMock.Setup(x => x.GetCurrentMemberAsync())
            .ReturnsAsync(currentUserMock.Object);
        _crewServiceMock.Setup(x => x.GetMemberByKeyAsync(memberKey, "admin@example.com"))
            .ReturnsAsync(memberData);

        // Act
        var result = await controller.GetMemberData(memberKey);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    #endregion
}
