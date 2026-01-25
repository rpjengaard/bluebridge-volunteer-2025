using Code.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;

namespace Tests.Services;

public class CrewServiceTests
{
    private readonly Mock<IMemberService> _memberServiceMock;
    private readonly Mock<IMemberGroupService> _memberGroupServiceMock;
    private readonly Mock<IContentService> _contentServiceMock;
    private readonly Mock<IUmbracoContextAccessor> _umbracoContextAccessorMock;
    private readonly Mock<ILogger<CrewService>> _loggerMock;
    private readonly CrewService _sut;

    public CrewServiceTests()
    {
        _memberServiceMock = new Mock<IMemberService>();
        _memberGroupServiceMock = new Mock<IMemberGroupService>();
        _contentServiceMock = new Mock<IContentService>();
        _umbracoContextAccessorMock = new Mock<IUmbracoContextAccessor>();
        _loggerMock = new Mock<ILogger<CrewService>>();

        _sut = new CrewService(
            _memberServiceMock.Object,
            _memberGroupServiceMock.Object,
            _contentServiceMock.Object,
            _umbracoContextAccessorMock.Object,
            _loggerMock.Object);
    }

    #region GetCrewsForMemberAsync Tests

    [Fact]
    public async Task GetCrewsForMemberAsync_WithNullEmail_ShouldReturnEmptyCrews()
    {
        // Arrange
        _memberServiceMock.Setup(x => x.GetByEmail(It.IsAny<string>()))
            .Returns((IMember?)null);

        // Act
        var result = await _sut.GetCrewsForMemberAsync(null!, false);

        // Assert
        result.Should().NotBeNull();
        result.Crews.Should().BeEmpty();
        result.IsAdmin.Should().BeFalse();
    }

    [Fact]
    public async Task GetCrewsForMemberAsync_WithNonExistentMember_ShouldReturnEmptyCrews()
    {
        // Arrange
        _memberServiceMock.Setup(x => x.GetByEmail(It.IsAny<string>()))
            .Returns((IMember?)null);

        // Act
        var result = await _sut.GetCrewsForMemberAsync("nonexistent@example.com", false);

        // Assert
        result.Should().NotBeNull();
        result.Crews.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCrewsForMemberAsync_WithAdminFlag_ShouldSetIsAdminTrue()
    {
        // Arrange
        var memberMock = new Mock<IMember>();
        memberMock.Setup(x => x.Id).Returns(1);
        memberMock.Setup(x => x.GetValue<string>("crews")).Returns((string?)null);

        _memberServiceMock.Setup(x => x.GetByEmail("admin@example.com"))
            .Returns(memberMock.Object);

        // Act
        var result = await _sut.GetCrewsForMemberAsync("admin@example.com", true);

        // Assert
        result.IsAdmin.Should().BeTrue();
    }

    #endregion

    #region GetCrewDetailAsync Tests

    [Fact]
    public async Task GetCrewDetailAsync_WithInvalidCrewId_ShouldReturnNull()
    {
        // Arrange
        _contentServiceMock.Setup(x => x.GetById(It.IsAny<int>()))
            .Returns((IContent?)null);

        // Act
        var result = await _sut.GetCrewDetailAsync(999, "test@example.com", CrewViewMode.Volunteer);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetMemberCrewViewModeAsync Tests

    [Fact]
    public async Task GetMemberCrewViewModeAsync_WithNullEmail_ShouldReturnVolunteer()
    {
        // Arrange
        _memberServiceMock.Setup(x => x.GetByEmail(It.IsAny<string>()))
            .Returns((IMember?)null);

        // Act
        var result = await _sut.GetMemberCrewViewModeAsync(null!, 1);

        // Assert
        result.Should().Be(CrewViewMode.Volunteer);
    }

    [Fact]
    public async Task GetMemberCrewViewModeAsync_WithNonExistentMember_ShouldReturnVolunteer()
    {
        // Arrange
        _memberServiceMock.Setup(x => x.GetByEmail("nonexistent@example.com"))
            .Returns((IMember?)null);

        // Act
        var result = await _sut.GetMemberCrewViewModeAsync("nonexistent@example.com", 1);

        // Assert
        result.Should().Be(CrewViewMode.Volunteer);
    }

    #endregion

    #region GetMemberByKeyAsync Tests

    [Fact]
    public async Task GetMemberByKeyAsync_WithEmptyGuid_ShouldReturnNull()
    {
        // Arrange
        _memberServiceMock.Setup(x => x.GetByKey(Guid.Empty))
            .Returns((IMember?)null);

        // Act
        var result = await _sut.GetMemberByKeyAsync(Guid.Empty, "requester@example.com");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetMemberByKeyAsync_WithNonExistentMember_ShouldReturnNull()
    {
        // Arrange
        var memberKey = Guid.NewGuid();
        _memberServiceMock.Setup(x => x.GetByKey(memberKey))
            .Returns((IMember?)null);

        // Act
        var result = await _sut.GetMemberByKeyAsync(memberKey, "requester@example.com");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region CrewViewMode Tests

    [Fact]
    public void CrewViewMode_ShouldHaveCorrectValues()
    {
        // Assert
        CrewViewMode.Volunteer.Should().Be((CrewViewMode)0);
        CrewViewMode.Scheduler.Should().Be((CrewViewMode)1);
        CrewViewMode.Admin.Should().Be((CrewViewMode)2);
    }

    #endregion

    #region CrewsPageData Model Tests

    [Fact]
    public void CrewsPageData_DefaultValues_ShouldBeInitialized()
    {
        // Arrange & Act
        var data = new CrewsPageData();

        // Assert
        data.IsAdmin.Should().BeFalse();
        data.Crews.Should().NotBeNull();
        data.Crews.Should().BeEmpty();
    }

    #endregion

    #region CrewListItem Model Tests

    [Fact]
    public void CrewListItem_DefaultValues_ShouldBeInitialized()
    {
        // Arrange & Act
        var item = new CrewListItem();

        // Assert
        item.Id.Should().Be(0);
        item.Key.Should().Be(Guid.Empty);
        item.Name.Should().BeEmpty();
        item.Description.Should().BeNull();
        item.AgeLimit.Should().BeNull();
        item.Url.Should().BeNull();
        item.MemberCount.Should().Be(0);
        item.IsMemberAssigned.Should().BeFalse();
    }

    #endregion

    #region CrewDetailData Model Tests

    [Fact]
    public void CrewDetailData_DefaultValues_ShouldBeInitialized()
    {
        // Arrange & Act
        var detail = new CrewDetailData();

        // Assert
        detail.Id.Should().Be(0);
        detail.Key.Should().Be(Guid.Empty);
        detail.Name.Should().BeEmpty();
        detail.Description.Should().BeNull();
        detail.DescriptionHtml.Should().BeNull();
        detail.AgeLimit.Should().BeNull();
        detail.Url.Should().BeNull();
        detail.ViewMode.Should().Be(CrewViewMode.Volunteer);
        detail.Members.Should().NotBeNull();
        detail.Members.Should().BeEmpty();
        detail.WishlistMembers.Should().NotBeNull();
        detail.WishlistMembers.Should().BeEmpty();
        detail.ScheduleSupervisor.Should().BeNull();
        detail.Supervisors.Should().NotBeNull();
        detail.Supervisors.Should().BeEmpty();
    }

    #endregion

    #region CrewMemberInfo Model Tests

    [Fact]
    public void CrewMemberInfo_DefaultValues_ShouldBeInitialized()
    {
        // Arrange & Act
        var info = new CrewMemberInfo();

        // Assert
        info.MemberId.Should().Be(0);
        info.MemberKey.Should().Be(Guid.Empty);
        info.FullName.Should().BeEmpty();
        info.Email.Should().BeEmpty();
        info.Phone.Should().BeNull();
        info.HasAccepted2026.Should().BeFalse();
    }

    #endregion

    #region SupervisorInfo Model Tests

    [Fact]
    public void SupervisorInfo_DefaultValues_ShouldBeInitialized()
    {
        // Arrange & Act
        var supervisor = new SupervisorInfo();

        // Assert
        supervisor.MemberId.Should().Be(0);
        supervisor.FullName.Should().BeEmpty();
        supervisor.Email.Should().BeEmpty();
        supervisor.Phone.Should().BeNull();
    }

    #endregion

    #region MemberDetailData Model Tests

    [Fact]
    public void MemberDetailData_DefaultValues_ShouldBeInitialized()
    {
        // Arrange & Act
        var detail = new MemberDetailData();

        // Assert
        detail.MemberId.Should().Be(0);
        detail.MemberKey.Should().Be(Guid.Empty);
        detail.FirstName.Should().BeEmpty();
        detail.LastName.Should().BeEmpty();
        detail.FullName.Should().BeEmpty();
        detail.Email.Should().BeEmpty();
        detail.Phone.Should().BeNull();
        detail.Birthdate.Should().BeNull();
        detail.TidligereArbejdssteder.Should().BeNull();
        detail.Accept2026.Should().BeFalse();
        detail.AcceptedDate.Should().BeNull();
        detail.InvitationSentDate.Should().BeNull();
        detail.AssignedCrews.Should().NotBeNull();
        detail.AssignedCrews.Should().BeEmpty();
        detail.CrewWishes.Should().NotBeNull();
        detail.CrewWishes.Should().BeEmpty();
        detail.MemberGroups.Should().NotBeNull();
        detail.MemberGroups.Should().BeEmpty();
    }

    #endregion
}
