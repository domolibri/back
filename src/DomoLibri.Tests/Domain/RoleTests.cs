using DomoLibri.Domain.Enums;

namespace DomoLibri.Tests.Domain;

public class RoleTests
{
    [Fact]
    public void Role_Admin_HasExpectedValue()
    {
        Assert.Equal(0, (int)Role.Admin);
    }

    [Fact]
    public void Role_Editor_HasExpectedValue()
    {
        Assert.Equal(1, (int)Role.Editor);
    }

    [Fact]
    public void Role_HasExactlyTwoValues()
    {
        var values = Enum.GetValues<Role>();
        Assert.Equal(2, values.Length);
    }

    [Fact]
    public void Role_ParseFromString_Works()
    {
        Assert.Equal(Role.Admin, Enum.Parse<Role>("Admin"));
        Assert.Equal(Role.Editor, Enum.Parse<Role>("Editor"));
    }

    [Fact]
    public void Role_ToString_ReturnsName()
    {
        Assert.Equal("Admin", Role.Admin.ToString());
        Assert.Equal("Editor", Role.Editor.ToString());
    }
}
