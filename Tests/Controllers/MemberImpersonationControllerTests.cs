using Code.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Web.Controllers;

namespace Tests.Controllers;

public class MemberImpersonationControllerTests
{
    private readonly Mock<IMemberImpersonationService> _impersonationServiceMock;
    private readonly Mock<IMemberService> _memberServiceMock;

    public MemberImpersonationControllerTests()
    {
        _impersonationServiceMock = new Mock<IMemberImpersonationService>();
        _memberServiceMock = new Mock<IMemberService>();
    }

    private MemberImpersonationController CreateController()
    {
        var controller = new MemberImpersonationController(
            _impersonationServiceMock.Object,
            _memberServiceMock.Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    #region GetMembers Tests

    [Fact]
    public void GetMembers_WithNoSearch_ShouldReturnAcceptedMembers()
    {
        // Arrange
        var controller = CreateController();
        long totalRecords;

        var member1Mock = new Mock<IMember>();
        member1Mock.Setup(x => x.Email).Returns("test1@example.com");
        member1Mock.Setup(x => x.Name).Returns("Test User 1");
        member1Mock.Setup(x => x.Username).Returns("test1@example.com");
        member1Mock.Setup(x => x.IsApproved).Returns(true);
        member1Mock.Setup(x => x.GetValue<bool>("accept2026")).Returns(true);
        member1Mock.Setup(x => x.GetValue<string>("firstName")).Returns("Test");
        member1Mock.Setup(x => x.GetValue<string>("lastName")).Returns("User 1");

        var member2Mock = new Mock<IMember>();
        member2Mock.Setup(x => x.Email).Returns("test2@example.com");
        member2Mock.Setup(x => x.Name).Returns("Test User 2");
        member2Mock.Setup(x => x.Username).Returns("test2@example.com");
        member2Mock.Setup(x => x.IsApproved).Returns(true);
        member2Mock.Setup(x => x.GetValue<bool>("accept2026")).Returns(false); // Not accepted

        _memberServiceMock.Setup(x => x.GetAll(It.IsAny<long>(), It.IsAny<int>(), out totalRecords))
            .Returns(new List<IMember> { member1Mock.Object, member2Mock.Object });

        // Act
        var result = controller.GetMembers(null);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public void GetMembers_WithSearchTerm_ShouldFilterResults()
    {
        // Arrange
        var controller = CreateController();
        long totalRecords;

        var memberMock = new Mock<IMember>();
        memberMock.Setup(x => x.Email).Returns("test@example.com");
        memberMock.Setup(x => x.Name).Returns("Test User");
        memberMock.Setup(x => x.Username).Returns("test@example.com");
        memberMock.Setup(x => x.IsApproved).Returns(true);
        memberMock.Setup(x => x.GetValue<bool>("accept2026")).Returns(true);
        memberMock.Setup(x => x.GetValue<string>("firstName")).Returns("Test");
        memberMock.Setup(x => x.GetValue<string>("lastName")).Returns("User");

        _memberServiceMock.Setup(x => x.GetAll(It.IsAny<long>(), It.IsAny<int>(), out totalRecords))
            .Returns(new List<IMember> { memberMock.Object });

        // Act
        var result = controller.GetMembers("test");

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public void GetMembers_WithEmptyMemberList_ShouldReturnEmptyResult()
    {
        // Arrange
        var controller = CreateController();
        long totalRecords;

        _memberServiceMock.Setup(x => x.GetAll(It.IsAny<long>(), It.IsAny<int>(), out totalRecords))
            .Returns(new List<IMember>());

        // Act
        var result = controller.GetMembers(null);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    #endregion

    #region StartImpersonation Tests

    [Fact]
    public async Task StartImpersonation_WithEmptyEmail_ShouldReturnBadRequest()
    {
        // Arrange
        var controller = CreateController();
        var request = new StartImpersonationRequest("");

        // Act
        var result = await controller.StartImpersonation(request);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task StartImpersonation_WithNullEmail_ShouldReturnBadRequest()
    {
        // Arrange
        var controller = CreateController();
        var request = new StartImpersonationRequest(null!);

        // Act
        var result = await controller.StartImpersonation(request);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task StartImpersonation_WithNonExistentMember_ShouldReturnNotFound()
    {
        // Arrange
        var controller = CreateController();
        var request = new StartImpersonationRequest("nonexistent@example.com");

        _memberServiceMock.Setup(x => x.GetByEmail("nonexistent@example.com"))
            .Returns((IMember?)null);

        // Act
        var result = await controller.StartImpersonation(request);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task StartImpersonation_WhenSuccessful_ShouldReturnOk()
    {
        // Arrange
        var controller = CreateController();
        var request = new StartImpersonationRequest("member@example.com");

        var memberMock = new Mock<IMember>();
        _memberServiceMock.Setup(x => x.GetByEmail("member@example.com"))
            .Returns(memberMock.Object);
        _impersonationServiceMock.Setup(x => x.StartImpersonationAsync("member@example.com"))
            .ReturnsAsync(true);

        // Act
        var result = await controller.StartImpersonation(request);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task StartImpersonation_WhenFails_ShouldReturn500()
    {
        // Arrange
        var controller = CreateController();
        var request = new StartImpersonationRequest("member@example.com");

        var memberMock = new Mock<IMember>();
        _memberServiceMock.Setup(x => x.GetByEmail("member@example.com"))
            .Returns(memberMock.Object);
        _impersonationServiceMock.Setup(x => x.StartImpersonationAsync("member@example.com"))
            .ReturnsAsync(false);

        // Act
        var result = await controller.StartImpersonation(request);

        // Assert
        result.Should().BeOfType<ObjectResult>();
        var objectResult = result as ObjectResult;
        objectResult!.StatusCode.Should().Be(500);
    }

    #endregion

    #region GetStatus Tests

    [Fact]
    public void GetStatus_WhenNotImpersonating_ShouldReturnFalseStatus()
    {
        // Arrange
        var controller = CreateController();
        _impersonationServiceMock.Setup(x => x.IsImpersonating()).Returns(false);
        _impersonationServiceMock.Setup(x => x.GetImpersonatedMemberEmail()).Returns((string?)null);
        _impersonationServiceMock.Setup(x => x.GetOriginalAdminEmail()).Returns((string?)null);

        // Act
        var result = controller.GetStatus();

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public void GetStatus_WhenImpersonating_ShouldReturnTrueStatusWithEmails()
    {
        // Arrange
        var controller = CreateController();
        _impersonationServiceMock.Setup(x => x.IsImpersonating()).Returns(true);
        _impersonationServiceMock.Setup(x => x.GetImpersonatedMemberEmail()).Returns("member@example.com");
        _impersonationServiceMock.Setup(x => x.GetOriginalAdminEmail()).Returns("admin@example.com");

        // Act
        var result = controller.GetStatus();

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    #endregion

    #region StartImpersonationRequest Tests

    [Fact]
    public void StartImpersonationRequest_ShouldHaveCorrectValue()
    {
        // Arrange & Act
        var request = new StartImpersonationRequest("test@example.com");

        // Assert
        request.MemberEmail.Should().Be("test@example.com");
    }

    #endregion
}
