using DomoLibri.Domain.Entities;

namespace DomoLibri.Tests.Domain;

public class RoleTests
{
    [Fact]
    public void Role_DefaultValues_AreCorrect()
    {
        var role = new Role();

        Assert.Equal(Guid.Empty, role.Id);
        Assert.Equal(Guid.Empty, role.EditoraId);
        Assert.Equal(string.Empty, role.Nome);
        Assert.Null(role.Descricao);
        Assert.NotNull(role.Permissions);
        Assert.Empty(role.Permissions);
        Assert.NotNull(role.Usuarios);
        Assert.Empty(role.Usuarios);
    }

    [Fact]
    public void Role_PropertiesCanBeSet()
    {
        var id = Guid.NewGuid();
        var editoraId = Guid.NewGuid();

        var role = new Role
        {
            Id = id,
            EditoraId = editoraId,
            Nome = "Administrador",
            Descricao = "Acesso total ao sistema"
        };

        Assert.Equal(id, role.Id);
        Assert.Equal(editoraId, role.EditoraId);
        Assert.Equal("Administrador", role.Nome);
        Assert.Equal("Acesso total ao sistema", role.Descricao);
    }

    [Fact]
    public void Role_Permissions_CanAddItems()
    {
        var role = new Role { Id = Guid.NewGuid(), Nome = "Editor" };
        var permission = new Permission { Id = Guid.NewGuid(), Codigo = "usuarios.ler", Nome = "Listar Usuários", Agrupamento = "Configurações" };

        role.Permissions.Add(permission);

        Assert.Single(role.Permissions);
        Assert.Contains(permission, role.Permissions);
    }

    [Fact]
    public void Role_Usuarios_CanAddItems()
    {
        var role = new Role { Id = Guid.NewGuid(), Nome = "Editor" };
        var usuario = new VinculoUsuarioEditora { Id = Guid.NewGuid(), EditoraId = Guid.NewGuid(), UsuarioId = Guid.NewGuid() };

        role.Usuarios.Add(usuario);

        Assert.Single(role.Usuarios);
        Assert.Contains(usuario, role.Usuarios);
    }
}
