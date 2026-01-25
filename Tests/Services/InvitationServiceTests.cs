using Code.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;

namespace Tests.Services;

public class InvitationServiceTests
{
    private readonly Mock<IMemberService> _memberServiceMock;
    private readonly Mock<IMemberGroupService> _memberGroupServiceMock;
    private readonly Mock<IMemberManager> _memberManagerMock;
    private readonly Mock<IContentService> _contentServiceMock;
    private readonly Mock<IMemberEmailService> _emailServiceMock;
    private readonly Mock<IUmbracoContextAccessor> _umbracoContextAccessorMock;
    private readonly Mock<ILogger<InvitationService>> _loggerMock;
    private readonly InvitationService _sut;

    public InvitationServiceTests()
    {
        _memberServiceMock = new Mock<IMemberService>();
        _memberGroupServiceMock = new Mock<IMemberGroupService>();
        _memberManagerMock = new Mock<IMemberManager>();
        _contentServiceMock = new Mock<IContentService>();
        _emailServiceMock = new Mock<IMemberEmailService>();
        _umbracoContextAccessorMock = new Mock<IUmbracoContextAccessor>();
        _loggerMock = new Mock<ILogger<InvitationService>>();

        _sut = new InvitationService(
            _memberServiceMock.Object,
            _memberGroupServiceMock.Object,
            _memberManagerMock.Object,
            _contentServiceMock.Object,
            _emailServiceMock.Object,
            _umbracoContextAccessorMock.Object,
            _loggerMock.Object);
    }

    #region GenerateInvitationTokenAsync Tests

    [Fact]
    public async Task GenerateInvitationTokenAsync_WithNonExistentMember_ShouldReturnEmptyString()
    {
        // Arrange
        _memberServiceMock.Setup(x => x.GetById(It.IsAny<int>()))
            .Returns((IMember?)null);

        // Act
        var result = await _sut.GenerateInvitationTokenAsync(999);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateInvitationTokenAsync_WithValidMember_ShouldReturnToken()
    {
        // Arrange
        var memberMock = new Mock<IMember>();
        memberMock.Setup(x => x.Id).Returns(1);

        _memberServiceMock.Setup(x => x.GetById(1))
            .Returns(memberMock.Object);

        // Act
        var result = await _sut.GenerateInvitationTokenAsync(1);

        // Assert
        result.Should().NotBeEmpty();
        Guid.TryParse(result, out _).Should().BeTrue();
    }

    #endregion

    #region SendInvitationAsync Tests

    [Fact]
    public async Task SendInvitationAsync_WithNonExistentMember_ShouldReturnFailure()
    {
        // Arrange
        _memberServiceMock.Setup(x => x.GetById(It.IsAny<int>()))
            .Returns((IMember?)null);

        // Act
        var result = await _sut.SendInvitationAsync(999, "https://example.com");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("ikke fundet");
    }

    [Fact]
    public async Task SendInvitationAsync_WithMemberWithoutEmail_ShouldReturnFailure()
    {
        // Arrange
        var memberMock = new Mock<IMember>();
        memberMock.Setup(x => x.Id).Returns(1);
        memberMock.Setup(x => x.Email).Returns((string?)null);

        _memberServiceMock.Setup(x => x.GetById(1))
            .Returns(memberMock.Object);

        // Act
        var result = await _sut.SendInvitationAsync(1, "https://example.com");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("email");
    }

    #endregion

    #region SendBulkInvitationsAsync Tests

    [Fact]
    public async Task SendBulkInvitationsAsync_WithNoMembers_ShouldReturnEmptyResult()
    {
        // Arrange
        _memberServiceMock.Setup(x => x.GetAll(It.IsAny<long>(), It.IsAny<int>(), out It.Ref<long>.IsAny))
            .Returns(new List<IMember>());

        // Act
        var result = await _sut.SendBulkInvitationsAsync("https://example.com");

        // Assert
        result.TotalMembers.Should().Be(0);
        result.SentCount.Should().Be(0);
        result.SkippedCount.Should().Be(0);
        result.ErrorCount.Should().Be(0);
    }

    #endregion

    #region GetMemberByTokenAsync Tests

    [Fact]
    public async Task GetMemberByTokenAsync_WithEmptyToken_ShouldReturnNull()
    {
        // Act
        var result = await _sut.GetMemberByTokenAsync("");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetMemberByTokenAsync_WithNullToken_ShouldReturnNull()
    {
        // Act
        var result = await _sut.GetMemberByTokenAsync(null!);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetMemberByTokenAsync_WithInvalidToken_ShouldReturnNull()
    {
        // Arrange
        _memberServiceMock.Setup(x => x.GetAll(It.IsAny<long>(), It.IsAny<int>(), out It.Ref<long>.IsAny))
            .Returns(new List<IMember>());

        // Act
        var result = await _sut.GetMemberByTokenAsync("invalid-token");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region AcceptInvitationAsync Tests

    [Fact]
    public async Task AcceptInvitationAsync_WithEmptyToken_ShouldReturnFailure()
    {
        // Act
        var result = await _sut.AcceptInvitationAsync("", new[] { 1 }, DateTime.Now, "password", "https://example.com");

        // Assert
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task AcceptInvitationAsync_WithNoCrewIds_ShouldReturnFailure()
    {
        // Arrange
        var memberMock = new Mock<IMember>();
        memberMock.Setup(x => x.Id).Returns(1);
        memberMock.Setup(x => x.GetValue<string>("invitationToken")).Returns("valid-token");

        _memberServiceMock.Setup(x => x.GetAll(It.IsAny<long>(), It.IsAny<int>(), out It.Ref<long>.IsAny))
            .Returns(new List<IMember> { memberMock.Object });

        // Act
        var result = await _sut.AcceptInvitationAsync("valid-token", Array.Empty<int>(), DateTime.Now, "password", "https://example.com");

        // Assert
        result.Success.Should().BeFalse();
    }

    #endregion

    #region GetInvitationStatusesAsync Tests

    [Fact]
    public async Task GetInvitationStatusesAsync_WithNoMembers_ShouldReturnEmptyList()
    {
        // Arrange
        _memberServiceMock.Setup(x => x.GetAll(It.IsAny<long>(), It.IsAny<int>(), out It.Ref<long>.IsAny))
            .Returns(new List<IMember>());

        // Act
        var result = await _sut.GetInvitationStatusesAsync();

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region GetAvailableCrewsAsync Tests

    [Fact]
    public async Task GetAvailableCrewsAsync_ShouldReturnCrews()
    {
        // Act
        var result = await _sut.GetAvailableCrewsAsync();

        // Assert
        result.Should().NotBeNull();
    }

    #endregion

    #region InvitationSendResult Model Tests

    [Fact]
    public void InvitationSendResult_DefaultValues_ShouldBeInitialized()
    {
        // Arrange & Act
        var result = new InvitationSendResult();

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().BeNull();
        result.Email.Should().BeNull();
    }

    #endregion

    #region BulkInvitationResult Model Tests

    [Fact]
    public void BulkInvitationResult_DefaultValues_ShouldBeInitialized()
    {
        // Arrange & Act
        var result = new BulkInvitationResult();

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().BeNull();
        result.TotalMembers.Should().Be(0);
        result.SentCount.Should().Be(0);
        result.SkippedCount.Should().Be(0);
        result.ErrorCount.Should().Be(0);
        result.Errors.Should().NotBeNull();
        result.Errors.Should().BeEmpty();
    }

    #endregion

    #region MemberInvitationInfo Model Tests

    [Fact]
    public void MemberInvitationInfo_DefaultValues_ShouldBeInitialized()
    {
        // Arrange & Act
        var info = new MemberInvitationInfo();

        // Assert
        info.MemberId.Should().Be(0);
        info.Email.Should().BeEmpty();
        info.FirstName.Should().BeEmpty();
        info.LastName.Should().BeEmpty();
        info.Birthdate.Should().BeNull();
        info.FullName.Should().BeEmpty();
    }

    [Fact]
    public void MemberInvitationInfo_FullName_ShouldCombineFirstAndLastName()
    {
        // Arrange & Act
        var info = new MemberInvitationInfo
        {
            FirstName = "Test",
            LastName = "User"
        };

        // Assert
        info.FullName.Should().Be("Test User");
    }

    #endregion

    #region AcceptInvitationResult Model Tests

    [Fact]
    public void AcceptInvitationResult_DefaultValues_ShouldBeInitialized()
    {
        // Arrange & Act
        var result = new AcceptInvitationResult();

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().BeNull();
        result.MemberName.Should().BeNull();
        result.SelectedCrewNames.Should().BeNull();
    }

    #endregion

    #region MemberInvitationStatus Model Tests

    [Fact]
    public void MemberInvitationStatus_DefaultValues_ShouldBeInitialized()
    {
        // Arrange & Act
        var status = new MemberInvitationStatus();

        // Assert
        status.MemberId.Should().Be(0);
        status.MemberKey.Should().Be(Guid.Empty);
        status.Email.Should().BeEmpty();
        status.FullName.Should().BeEmpty();
        status.Status.Should().Be("NotInvited");
        status.InvitationSentDate.Should().BeNull();
        status.AcceptedDate.Should().BeNull();
    }

    #endregion

    #region CrewInfo Model Tests

    [Fact]
    public void CrewInfo_DefaultValues_ShouldBeInitialized()
    {
        // Arrange & Act
        var info = new CrewInfo();

        // Assert
        info.Id.Should().Be(0);
        info.Key.Should().Be(Guid.Empty);
        info.Name.Should().BeEmpty();
        info.Description.Should().BeNull();
        info.AgeLimit.Should().BeNull();
    }

    #endregion
}
