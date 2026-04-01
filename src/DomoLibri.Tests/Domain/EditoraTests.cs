using DomoLibri.Domain.Entities;
using DomoLibri.Domain.Enums;

namespace DomoLibri.Tests.Domain;

public class EditoraTests
{
    [Fact]
    public void Editora_DefaultValues_AreCorrect()
    {
        var editora = new Editora();

        Assert.Equal(Guid.Empty, editora.Id);
        Assert.Equal(string.Empty, editora.Nome);
        Assert.Equal(string.Empty, editora.Slug);
        Assert.Null(editora.LogoUrl);
        Assert.Null(editora.CorPrimaria);
        Assert.Equal(default, editora.DataCriacao);
        Assert.False(editora.Ativo);
        Assert.NotNull(editora.Usuarios);
        Assert.Empty(editora.Usuarios);
    }

    [Fact]
    public void Editora_PropertiesCanBeSet()
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var editora = new Editora
        {
            Id = id,
            Nome = "Editora Teste",
            Slug = "editora-teste",
            LogoUrl = "https://storage.example.com/logo.png",
            CorPrimaria = "#0078D4",
            DataCriacao = now,
            Ativo = true
        };

        Assert.Equal(id, editora.Id);
        Assert.Equal("Editora Teste", editora.Nome);
        Assert.Equal("editora-teste", editora.Slug);
        Assert.Equal("https://storage.example.com/logo.png", editora.LogoUrl);
        Assert.Equal("#0078D4", editora.CorPrimaria);
        Assert.Equal(now, editora.DataCriacao);
        Assert.True(editora.Ativo);
    }

    [Fact]
    public void Editora_Usuarios_CanAddItems()
    {
        var editoraId = Guid.NewGuid();
        var editora = new Editora { Id = editoraId };
        var usuario = new UsuarioEditora
        {
            Id = Guid.NewGuid(),
            EditoraId = editoraId,
            Email = "admin@teste.com",
            Role = Role.Admin
        };

        editora.Usuarios.Add(usuario);

        Assert.Single(editora.Usuarios);
        Assert.Contains(usuario, editora.Usuarios);
    }

    [Fact]
    public void Editora_LogoUrl_CanBeNull()
    {
        var editora = new Editora { LogoUrl = null };
        Assert.Null(editora.LogoUrl);
    }

    [Fact]
    public void Editora_CorPrimaria_CanBeNull()
    {
        var editora = new Editora { CorPrimaria = null };
        Assert.Null(editora.CorPrimaria);
    }
}
