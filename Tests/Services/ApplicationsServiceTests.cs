using Code.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;

namespace Tests.Services;

public class ApplicationsServiceTests
{
    private readonly Mock<IMemberService> _memberServiceMock;
    private readonly Mock<IMemberGroupService> _memberGroupServiceMock;
    private readonly Mock<IContentService> _contentServiceMock;
    private readonly Mock<IUmbracoContextAccessor> _umbracoContextAccessorMock;
    private readonly Mock<ILogger<ApplicationsService>> _loggerMock;
    private readonly ApplicationsService _sut;

    public ApplicationsServiceTests()
    {
        _memberServiceMock = new Mock<IMemberService>();
        _memberGroupServiceMock = new Mock<IMemberGroupService>();
        _contentServiceMock = new Mock<IContentService>();
        _umbracoContextAccessorMock = new Mock<IUmbracoContextAccessor>();
        _loggerMock = new Mock<ILogger<ApplicationsService>>();

        _sut = new ApplicationsService(
            _memberServiceMock.Object,
            _memberGroupServiceMock.Object,
            _contentServiceMock.Object,
            _umbracoContextAccessorMock.Object,
            _loggerMock.Object);
    }

    #region GetApplicationsForMemberAsync Tests

    [Fact]
    public async Task GetApplicationsForMemberAsync_WithNullEmail_ShouldReturnEmptyApplications()
    {
        // Arrange
        _memberServiceMock.Setup(x => x.GetByEmail(It.IsAny<string>()))
            .Returns((IMember?)null);

        // Act
        var result = await _sut.GetApplicationsForMemberAsync(null!);

        // Assert
        result.Should().NotBeNull();
        result.Applications.Should().BeEmpty();
        result.IsAdmin.Should().BeFalse();
        result.IsScheduler.Should().BeFalse();
    }

    [Fact]
    public async Task GetApplicationsForMemberAsync_WithNonExistentMember_ShouldReturnEmptyApplications()
    {
        // Arrange
        _memberServiceMock.Setup(x => x.GetByEmail("nonexistent@example.com"))
            .Returns((IMember?)null);

        // Act
        var result = await _sut.GetApplicationsForMemberAsync("nonexistent@example.com");

        // Assert
        result.Should().NotBeNull();
        result.Applications.Should().BeEmpty();
    }

    [Fact]
    public async Task GetApplicationsForMemberAsync_WithValidMember_ShouldReturnApplicationsPageData()
    {
        // Arrange
        var memberMock = new Mock<IMember>();
        memberMock.Setup(x => x.Id).Returns(1);
        memberMock.Setup(x => x.Key).Returns(Guid.NewGuid());

        _memberServiceMock.Setup(x => x.GetByEmail("member@example.com"))
            .Returns(memberMock.Object);
        _memberServiceMock.Setup(x => x.GetAll(It.IsAny<long>(), It.IsAny<int>(), out It.Ref<long>.IsAny))
            .Returns(new List<IMember>());

        // Act
        var result = await _sut.GetApplicationsForMemberAsync("member@example.com");

        // Assert
        result.Should().NotBeNull();
        result.Applications.Should().NotBeNull();
        result.AllowedCrews.Should().NotBeNull();
    }

    #endregion

    #region ApplicationsPageData Model Tests

    [Fact]
    public void ApplicationsPageData_DefaultValues_ShouldBeInitialized()
    {
        // Arrange & Act
        var data = new ApplicationsPageData();

        // Assert
        data.IsAdmin.Should().BeFalse();
        data.IsScheduler.Should().BeFalse();
        data.Applications.Should().NotBeNull();
        data.Applications.Should().BeEmpty();
        data.AllowedCrews.Should().NotBeNull();
        data.AllowedCrews.Should().BeEmpty();
    }

    #endregion

    #region ApplicationInfo Model Tests

    [Fact]
    public void ApplicationInfo_DefaultValues_ShouldBeInitialized()
    {
        // Arrange & Act
        var info = new ApplicationInfo();

        // Assert
        info.MemberId.Should().Be(0);
        info.MemberKey.Should().Be(Guid.Empty);
        info.FirstName.Should().BeEmpty();
        info.LastName.Should().BeEmpty();
        info.FullName.Should().BeEmpty();
        info.Email.Should().BeEmpty();
        info.Phone.Should().BeNull();
        info.Birthdate.Should().BeNull();
        info.Age.Should().BeNull();
        info.Zipcode.Should().BeNull();
        info.TidligereArbejdssteder.Should().BeNull();
        info.AcceptedDate.Should().BeNull();
        info.CrewWishes.Should().NotBeNull();
        info.CrewWishes.Should().BeEmpty();
    }

    [Fact]
    public void ApplicationInfo_WithValues_ShouldRetainValues()
    {
        // Arrange & Act
        var memberKey = Guid.NewGuid();
        var acceptedDate = DateTime.Now;
        var info = new ApplicationInfo
        {
            MemberId = 1,
            MemberKey = memberKey,
            FirstName = "Test",
            LastName = "User",
            FullName = "Test User",
            Email = "test@example.com",
            Phone = "12345678",
            Birthdate = new DateTime(1990, 1, 1),
            Age = 34,
            Zipcode = "1234",
            TidligereArbejdssteder = "Previous workplace",
            AcceptedDate = acceptedDate,
            CrewWishes = new List<CrewListItem>
            {
                new() { Id = 1, Name = "Crew 1" }
            }
        };

        // Assert
        info.MemberId.Should().Be(1);
        info.MemberKey.Should().Be(memberKey);
        info.FirstName.Should().Be("Test");
        info.LastName.Should().Be("User");
        info.FullName.Should().Be("Test User");
        info.Email.Should().Be("test@example.com");
        info.Phone.Should().Be("12345678");
        info.Birthdate.Should().Be(new DateTime(1990, 1, 1));
        info.Age.Should().Be(34);
        info.Zipcode.Should().Be("1234");
        info.TidligereArbejdssteder.Should().Be("Previous workplace");
        info.AcceptedDate.Should().Be(acceptedDate);
        info.CrewWishes.Should().HaveCount(1);
    }

    #endregion
}
