using EncryptionFunctionApp.Constants;
using FluentAssertions;

namespace EncryptionFunctionApp.Tests.Constants;

public class FileTypesTests
{
    [Fact]
    public void Members_ConstantValue_IsCorrect()
        => FileTypes.Members.Should().Be("members");

    [Fact]
    public void Addresses_ConstantValue_IsCorrect()
        => FileTypes.Addresses.Should().Be("addresses");

    [Fact]
    public void All_ContainsMembers()
        => FileTypes.All.Should().Contain(FileTypes.Members);

    [Fact]
    public void All_ContainsAddresses()
        => FileTypes.All.Should().Contain(FileTypes.Addresses);

    [Theory]
    [InlineData("members")]
    [InlineData("MEMBERS")]
    [InlineData("Members")]
    [InlineData("ADDRESSES")]
    [InlineData("Addresses")]
    public void All_IsCaseInsensitive(string value)
        => FileTypes.All.Contains(value).Should().BeTrue();

    [Theory]
    [InlineData("invoices")]
    [InlineData("payments")]
    [InlineData("")]
    [InlineData(" ")]
    public void All_DoesNotContainUnknownTypes(string value)
        => FileTypes.All.Contains(value).Should().BeFalse();
}
