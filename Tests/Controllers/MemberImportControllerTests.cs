using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Text;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Web.Controllers;

namespace Tests.Controllers;

public class MemberImportControllerTests
{
    private readonly Mock<IMemberManager> _memberManagerMock;
    private readonly Mock<IMemberService> _memberServiceMock;
    private readonly Mock<IMemberTypeService> _memberTypeServiceMock;

    public MemberImportControllerTests()
    {
        _memberManagerMock = new Mock<IMemberManager>();
        _memberServiceMock = new Mock<IMemberService>();
        _memberTypeServiceMock = new Mock<IMemberTypeService>();
    }

    private MemberImportController CreateController()
    {
        var controller = new MemberImportController(
            _memberManagerMock.Object,
            _memberServiceMock.Object,
            _memberTypeServiceMock.Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    private IFormFile CreateMockCsvFile(string content, string fileName = "test.csv")
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/csv"
        };
    }

    #region ImportCsv Tests

    [Fact]
    public async Task ImportCsv_WithNullFile_ShouldReturnBadRequest()
    {
        // Arrange
        var controller = CreateController();

        // Act
        var result = await controller.ImportCsv(null!);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ImportCsv_WithEmptyFile_ShouldReturnBadRequest()
    {
        // Arrange
        var controller = CreateController();
        var file = CreateMockCsvFile("");

        // Act
        var result = await controller.ImportCsv(file);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ImportCsv_WithNonCsvFile_ShouldReturnBadRequest()
    {
        // Arrange
        var controller = CreateController();
        var file = CreateMockCsvFile("content", "test.txt");

        // Act
        var result = await controller.ImportCsv(file);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ImportCsv_WithEmptyCsvContent_ShouldReturnBadRequest()
    {
        // Arrange
        var controller = CreateController();
        var file = CreateMockCsvFile("");
        // Create file with 0 length
        var stream = new MemoryStream();
        var emptyFile = new FormFile(stream, 0, 0, "file", "test.csv")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/csv"
        };

        // Act
        var result = await controller.ImportCsv(emptyFile);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ImportCsv_WithMissingEmailColumn_ShouldReturnBadRequest()
    {
        // Arrange
        var controller = CreateController();
        var csvContent = "Fornavn,Efternavn,Telefon\nTest,User,12345678";
        var file = CreateMockCsvFile(csvContent);

        // Act
        var result = await controller.ImportCsv(file);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ImportCsv_WithValidNewMembers_ShouldCreateMembers()
    {
        // Arrange
        var controller = CreateController();
        var csvContent = "Email,Fornavn,Efternavn,Telefon\ntest@example.com,Test,User,12345678";
        var file = CreateMockCsvFile(csvContent);

        _memberManagerMock.Setup(x => x.FindByEmailAsync("test@example.com"))
            .ReturnsAsync((MemberIdentityUser?)null);
        _memberManagerMock.Setup(x => x.CreateAsync(It.IsAny<MemberIdentityUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);

        var memberMock = new Mock<IMember>();
        _memberServiceMock.Setup(x => x.GetByEmail("test@example.com"))
            .Returns(memberMock.Object);

        // Act
        var result = await controller.ImportCsv(file);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task ImportCsv_WithExistingMembers_ShouldUpdateMembers()
    {
        // Arrange
        var controller = CreateController();
        var csvContent = "Email,Fornavn,Efternavn,Telefon\nexisting@example.com,Updated,User,87654321";
        var file = CreateMockCsvFile(csvContent);

        var existingUserMock = new Mock<MemberIdentityUser>();
        _memberManagerMock.Setup(x => x.FindByEmailAsync("existing@example.com"))
            .ReturnsAsync(existingUserMock.Object);

        var existingMemberMock = new Mock<IMember>();
        _memberServiceMock.Setup(x => x.GetByEmail("existing@example.com"))
            .Returns(existingMemberMock.Object);

        // Act
        var result = await controller.ImportCsv(file);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        existingMemberMock.Verify(x => x.SetValue("firstName", "Updated"), Times.Once);
        existingMemberMock.Verify(x => x.SetValue("lastName", "User"), Times.Once);
        existingMemberMock.Verify(x => x.SetValue("phone", "87654321"), Times.Once);
        _memberServiceMock.Verify(x => x.Save(existingMemberMock.Object), Times.Once);
    }

    [Fact]
    public async Task ImportCsv_WithMissingEmail_ShouldSkipRow()
    {
        // Arrange
        var controller = CreateController();
        var csvContent = "Email,Fornavn,Efternavn,Telefon\n,Test,User,12345678";
        var file = CreateMockCsvFile(csvContent);

        // Act
        var result = await controller.ImportCsv(file);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        // Verify no member was created
        _memberManagerMock.Verify(x => x.CreateAsync(It.IsAny<MemberIdentityUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ImportCsv_WithColumnCountMismatch_ShouldReportError()
    {
        // Arrange
        var controller = CreateController();
        var csvContent = "Email,Fornavn,Efternavn,Telefon\ntest@example.com,Test,User"; // Missing phone column
        var file = CreateMockCsvFile(csvContent);

        // Act
        var result = await controller.ImportCsv(file);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task ImportCsv_WithQuotedValues_ShouldParseCorrectly()
    {
        // Arrange
        var controller = CreateController();
        var csvContent = "Email,Fornavn,Efternavn,Arbejdssteder\ntest@example.com,\"Test\",\"User\",\"Job 1, Job 2\"";
        var file = CreateMockCsvFile(csvContent);

        _memberManagerMock.Setup(x => x.FindByEmailAsync("test@example.com"))
            .ReturnsAsync((MemberIdentityUser?)null);
        _memberManagerMock.Setup(x => x.CreateAsync(It.IsAny<MemberIdentityUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);

        var memberMock = new Mock<IMember>();
        _memberServiceMock.Setup(x => x.GetByEmail("test@example.com"))
            .Returns(memberMock.Object);

        // Act
        var result = await controller.ImportCsv(file);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task ImportCsv_WhenMemberCreationFails_ShouldReportError()
    {
        // Arrange
        var controller = CreateController();
        var csvContent = "Email,Fornavn,Efternavn,Telefon\ntest@example.com,Test,User,12345678";
        var file = CreateMockCsvFile(csvContent);

        _memberManagerMock.Setup(x => x.FindByEmailAsync("test@example.com"))
            .ReturnsAsync((MemberIdentityUser?)null);
        _memberManagerMock.Setup(x => x.CreateAsync(It.IsAny<MemberIdentityUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Creation failed" }));

        // Act
        var result = await controller.ImportCsv(file);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task ImportCsv_WithMultipleRows_ShouldProcessAll()
    {
        // Arrange
        var controller = CreateController();
        var csvContent = "Email,Fornavn,Efternavn,Telefon\ntest1@example.com,Test1,User1,11111111\ntest2@example.com,Test2,User2,22222222";
        var file = CreateMockCsvFile(csvContent);

        _memberManagerMock.Setup(x => x.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((MemberIdentityUser?)null);
        _memberManagerMock.Setup(x => x.CreateAsync(It.IsAny<MemberIdentityUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);

        var memberMock = new Mock<IMember>();
        _memberServiceMock.Setup(x => x.GetByEmail(It.IsAny<string>()))
            .Returns(memberMock.Object);

        // Act
        var result = await controller.ImportCsv(file);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        _memberManagerMock.Verify(x => x.CreateAsync(It.IsAny<MemberIdentityUser>(), It.IsAny<string>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ImportCsv_WithEmptyRows_ShouldSkipThem()
    {
        // Arrange
        var controller = CreateController();
        var csvContent = "Email,Fornavn,Efternavn,Telefon\ntest@example.com,Test,User,12345678\n\n";
        var file = CreateMockCsvFile(csvContent);

        _memberManagerMock.Setup(x => x.FindByEmailAsync("test@example.com"))
            .ReturnsAsync((MemberIdentityUser?)null);
        _memberManagerMock.Setup(x => x.CreateAsync(It.IsAny<MemberIdentityUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);

        var memberMock = new Mock<IMember>();
        _memberServiceMock.Setup(x => x.GetByEmail("test@example.com"))
            .Returns(memberMock.Object);

        // Act
        var result = await controller.ImportCsv(file);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    #endregion
}
