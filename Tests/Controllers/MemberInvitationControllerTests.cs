using Code.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Web.Controllers;

namespace Tests.Controllers;

public class MemberInvitationControllerTests
{
    private readonly Mock<IInvitationService> _invitationServiceMock;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;

    public MemberInvitationControllerTests()
    {
        _invitationServiceMock = new Mock<IInvitationService>();
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
    }

    private MemberInvitationController CreateController()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("example.com");

        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        var controller = new MemberInvitationController(
            _invitationServiceMock.Object,
            _httpContextAccessorMock.Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        return controller;
    }

    #region GetMembers Tests

    [Fact]
    public async Task GetMembers_ShouldReturnOkWithMembers()
    {
        // Arrange
        var controller = CreateController();
        var members = new List<MemberInvitationStatus>
        {
            new() { MemberId = 1, Email = "test1@example.com", FullName = "Test User 1", Status = "NotInvited" },
            new() { MemberId = 2, Email = "test2@example.com", FullName = "Test User 2", Status = "Invited" }
        };

        _invitationServiceMock.Setup(x => x.GetInvitationStatusesAsync())
            .ReturnsAsync(members);

        // Act
        var result = await controller.GetMembers();

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetMembers_WhenExceptionThrown_ShouldReturn500()
    {
        // Arrange
        var controller = CreateController();
        _invitationServiceMock.Setup(x => x.GetInvitationStatusesAsync())
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await controller.GetMembers();

        // Assert
        result.Should().BeOfType<ObjectResult>();
        var objectResult = result as ObjectResult;
        objectResult!.StatusCode.Should().Be(500);
    }

    #endregion

    #region GetCrews Tests

    [Fact]
    public async Task GetCrews_ShouldReturnOkWithCrews()
    {
        // Arrange
        var controller = CreateController();
        var crews = new List<CrewInfo>
        {
            new() { Id = 1, Name = "Crew 1" },
            new() { Id = 2, Name = "Crew 2" }
        };

        _invitationServiceMock.Setup(x => x.GetAvailableCrewsAsync())
            .ReturnsAsync(crews);

        // Act
        var result = await controller.GetCrews();

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetCrews_WhenExceptionThrown_ShouldReturn500()
    {
        // Arrange
        var controller = CreateController();
        _invitationServiceMock.Setup(x => x.GetAvailableCrewsAsync())
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await controller.GetCrews();

        // Assert
        result.Should().BeOfType<ObjectResult>();
        var objectResult = result as ObjectResult;
        objectResult!.StatusCode.Should().Be(500);
    }

    #endregion

    #region InviteMember Tests

    [Fact]
    public async Task InviteMember_WithSuccessfulInvitation_ShouldReturnOk()
    {
        // Arrange
        var controller = CreateController();
        _invitationServiceMock.Setup(x => x.SendInvitationAsync(1, It.IsAny<string>()))
            .ReturnsAsync(new InvitationSendResult
            {
                Success = true,
                Message = "Invitation sent",
                Email = "test@example.com"
            });

        // Act
        var result = await controller.InviteMember(1);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task InviteMember_WithFailedInvitation_ShouldReturnBadRequest()
    {
        // Arrange
        var controller = CreateController();
        _invitationServiceMock.Setup(x => x.SendInvitationAsync(1, It.IsAny<string>()))
            .ReturnsAsync(new InvitationSendResult
            {
                Success = false,
                Message = "Member not found",
                Email = null
            });

        // Act
        var result = await controller.InviteMember(1);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task InviteMember_WhenExceptionThrown_ShouldReturn500()
    {
        // Arrange
        var controller = CreateController();
        _invitationServiceMock.Setup(x => x.SendInvitationAsync(1, It.IsAny<string>()))
            .ThrowsAsync(new Exception("Email service error"));

        // Act
        var result = await controller.InviteMember(1);

        // Assert
        result.Should().BeOfType<ObjectResult>();
        var objectResult = result as ObjectResult;
        objectResult!.StatusCode.Should().Be(500);
    }

    #endregion

    #region InviteAllMembers Tests

    [Fact]
    public async Task InviteAllMembers_ShouldReturnOkWithResults()
    {
        // Arrange
        var controller = CreateController();
        _invitationServiceMock.Setup(x => x.SendBulkInvitationsAsync(It.IsAny<string>()))
            .ReturnsAsync(new BulkInvitationResult
            {
                Success = true,
                Message = "Invitations sent",
                TotalMembers = 10,
                SentCount = 8,
                SkippedCount = 2,
                ErrorCount = 0,
                Errors = new List<string>()
            });

        // Act
        var result = await controller.InviteAllMembers();

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task InviteAllMembers_WithErrors_ShouldReturnOkWithErrorDetails()
    {
        // Arrange
        var controller = CreateController();
        _invitationServiceMock.Setup(x => x.SendBulkInvitationsAsync(It.IsAny<string>()))
            .ReturnsAsync(new BulkInvitationResult
            {
                Success = true,
                Message = "Invitations sent with some errors",
                TotalMembers = 10,
                SentCount = 7,
                SkippedCount = 1,
                ErrorCount = 2,
                Errors = new List<string> { "Error 1", "Error 2" }
            });

        // Act
        var result = await controller.InviteAllMembers();

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task InviteAllMembers_WhenExceptionThrown_ShouldReturn500()
    {
        // Arrange
        var controller = CreateController();
        _invitationServiceMock.Setup(x => x.SendBulkInvitationsAsync(It.IsAny<string>()))
            .ThrowsAsync(new Exception("Email service error"));

        // Act
        var result = await controller.InviteAllMembers();

        // Assert
        result.Should().BeOfType<ObjectResult>();
        var objectResult = result as ObjectResult;
        objectResult!.StatusCode.Should().Be(500);
    }

    #endregion
}
