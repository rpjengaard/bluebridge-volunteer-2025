using Code.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;

namespace Tests.Services;

public class DashboardServiceTests
{
    private readonly Mock<IMemberService> _memberServiceMock;
    private readonly Mock<IContentService> _contentServiceMock;
    private readonly Mock<IUmbracoContextAccessor> _umbracoContextAccessorMock;
    private readonly Mock<ILogger<DashboardService>> _loggerMock;
    private readonly DashboardService _sut;

    public DashboardServiceTests()
    {
        _memberServiceMock = new Mock<IMemberService>();
        _contentServiceMock = new Mock<IContentService>();
        _umbracoContextAccessorMock = new Mock<IUmbracoContextAccessor>();
        _loggerMock = new Mock<ILogger<DashboardService>>();

        _sut = new DashboardService(
            _memberServiceMock.Object,
            _contentServiceMock.Object,
            _umbracoContextAccessorMock.Object,
            _loggerMock.Object);
    }

    #region GetDashboardDataAsync Tests

    [Fact]
    public async Task GetDashboardDataAsync_WithNullEmail_ShouldReturnNull()
    {
        // Act
        var result = await _sut.GetDashboardDataAsync(null!);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetDashboardDataAsync_WithEmptyEmail_ShouldReturnNull()
    {
        // Act
        var result = await _sut.GetDashboardDataAsync("");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetDashboardDataAsync_WithWhitespaceEmail_ShouldReturnNull()
    {
        // Act
        var result = await _sut.GetDashboardDataAsync("   ");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetDashboardDataAsync_WithNonExistentMember_ShouldReturnNull()
    {
        // Arrange
        _memberServiceMock.Setup(x => x.GetByEmail(It.IsAny<string>()))
            .Returns((IMember?)null);

        // Act
        var result = await _sut.GetDashboardDataAsync("nonexistent@example.com");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetDashboardDataAsync_WithValidMember_ShouldReturnDashboardData()
    {
        // Arrange
        var memberMock = new Mock<IMember>();
        memberMock.Setup(x => x.Id).Returns(1);
        memberMock.Setup(x => x.Email).Returns("test@example.com");
        memberMock.Setup(x => x.GetValue<string>("firstName")).Returns("Test");
        memberMock.Setup(x => x.GetValue<string>("lastName")).Returns("User");
        memberMock.Setup(x => x.GetValue<string>("phone")).Returns("12345678");
        memberMock.Setup(x => x.GetValue<DateTime?>("birthdate")).Returns(new DateTime(1990, 1, 1));
        memberMock.Setup(x => x.GetValue<string>("tidligereArbejdssteder")).Returns("Previous workplace");
        memberMock.Setup(x => x.GetValue<bool>("accept2026")).Returns(true);
        memberMock.Setup(x => x.GetValue<DateTime?>("acceptedDate")).Returns(new DateTime(2024, 6, 1));
        memberMock.Setup(x => x.GetValue<string>("crewWishes")).Returns((string?)null);
        memberMock.Setup(x => x.GetValue<string>("crews")).Returns((string?)null);

        _memberServiceMock.Setup(x => x.GetByEmail("test@example.com"))
            .Returns(memberMock.Object);

        // Act
        var result = await _sut.GetDashboardDataAsync("test@example.com");

        // Assert
        result.Should().NotBeNull();
        result!.Profile.MemberId.Should().Be(1);
        result.Profile.FirstName.Should().Be("Test");
        result.Profile.LastName.Should().Be("User");
        result.Profile.Email.Should().Be("test@example.com");
        result.Profile.Phone.Should().Be("12345678");
        result.Profile.Birthdate.Should().Be(new DateTime(1990, 1, 1));
        result.Profile.PreviousWorkplaces.Should().Be("Previous workplace");
        result.HasAccepted2026.Should().BeTrue();
        result.AcceptedDate.Should().Be(new DateTime(2024, 6, 1));
        result.CrewWishes.Should().BeEmpty();
        result.AssignedCrews.Should().BeEmpty();
        result.Shifts.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDashboardDataAsync_WithNullMemberProperties_ShouldHandleGracefully()
    {
        // Arrange
        var memberMock = new Mock<IMember>();
        memberMock.Setup(x => x.Id).Returns(1);
        memberMock.Setup(x => x.Email).Returns((string?)null);
        memberMock.Setup(x => x.GetValue<string>("firstName")).Returns((string?)null);
        memberMock.Setup(x => x.GetValue<string>("lastName")).Returns((string?)null);
        memberMock.Setup(x => x.GetValue<string>("phone")).Returns((string?)null);
        memberMock.Setup(x => x.GetValue<DateTime?>("birthdate")).Returns((DateTime?)null);
        memberMock.Setup(x => x.GetValue<string>("tidligereArbejdssteder")).Returns((string?)null);
        memberMock.Setup(x => x.GetValue<bool>("accept2026")).Returns(false);
        memberMock.Setup(x => x.GetValue<DateTime?>("acceptedDate")).Returns((DateTime?)null);
        memberMock.Setup(x => x.GetValue<string>("crewWishes")).Returns((string?)null);
        memberMock.Setup(x => x.GetValue<string>("crews")).Returns((string?)null);

        _memberServiceMock.Setup(x => x.GetByEmail("test@example.com"))
            .Returns(memberMock.Object);

        // Act
        var result = await _sut.GetDashboardDataAsync("test@example.com");

        // Assert
        result.Should().NotBeNull();
        result!.Profile.FirstName.Should().BeEmpty();
        result.Profile.LastName.Should().BeEmpty();
        result.Profile.Email.Should().BeEmpty();
        result.Profile.Phone.Should().BeNull();
        result.Profile.Birthdate.Should().BeNull();
        result.HasAccepted2026.Should().BeFalse();
        result.AcceptedDate.Should().BeNull();
    }

    #endregion

    #region DashboardData Model Tests

    [Fact]
    public void DashboardData_DefaultValues_ShouldBeInitialized()
    {
        // Arrange & Act
        var data = new DashboardData();

        // Assert
        data.Profile.Should().NotBeNull();
        data.CrewWishes.Should().NotBeNull();
        data.CrewWishes.Should().BeEmpty();
        data.AssignedCrews.Should().NotBeNull();
        data.AssignedCrews.Should().BeEmpty();
        data.Shifts.Should().NotBeNull();
        data.Shifts.Should().BeEmpty();
        data.HasAccepted2026.Should().BeFalse();
        data.AcceptedDate.Should().BeNull();
    }

    #endregion

    #region MemberProfileData Model Tests

    [Fact]
    public void MemberProfileData_DefaultValues_ShouldBeInitialized()
    {
        // Arrange & Act
        var profile = new MemberProfileData();

        // Assert
        profile.MemberId.Should().Be(0);
        profile.FirstName.Should().BeEmpty();
        profile.LastName.Should().BeEmpty();
        profile.Email.Should().BeEmpty();
        profile.Phone.Should().BeNull();
        profile.Birthdate.Should().BeNull();
        profile.PreviousWorkplaces.Should().BeNull();
    }

    #endregion

    #region CrewData Model Tests

    [Fact]
    public void CrewData_DefaultValues_ShouldBeInitialized()
    {
        // Arrange & Act
        var crew = new CrewData();

        // Assert
        crew.Id.Should().Be(0);
        crew.Key.Should().Be(Guid.Empty);
        crew.Name.Should().BeEmpty();
        crew.Description.Should().BeNull();
        crew.AgeLimit.Should().BeNull();
        crew.Url.Should().BeNull();
    }

    #endregion

    #region ShiftData Model Tests

    [Fact]
    public void ShiftData_DefaultValues_ShouldBeInitialized()
    {
        // Arrange & Act
        var shift = new ShiftData();

        // Assert
        shift.Id.Should().Be(0);
        shift.CrewName.Should().BeEmpty();
        shift.StartTime.Should().Be(default);
        shift.EndTime.Should().Be(default);
        shift.Location.Should().BeNull();
        shift.Notes.Should().BeNull();
    }

    #endregion
}
