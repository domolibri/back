using DomoLibri.Domain.Entities;

namespace DomoLibri.Tests.Domain;

public class EditoraTests
{
    [Fact]
    public void Editora_Constructor_SetsPropertiesCorrectly()
    {
        var editora = new Editora("Editora Teste", "editora-teste");

        Assert.NotEqual(Guid.Empty, editora.Id);
        Assert.Equal("Editora Teste", editora.Nome);
        Assert.Equal("editora-teste", editora.Slug);
        Assert.Null(editora.LogoUrl);
        Assert.Null(editora.CorPrimaria);
        Assert.True(editora.DataCriacao <= DateTime.UtcNow);
        Assert.True(editora.DataCriacao > DateTime.UtcNow.AddSeconds(-5));
        Assert.True(editora.Ativo);
        Assert.NotNull(editora.Usuarios);
        Assert.Empty(editora.Usuarios);
    }

    [Fact]
    public void Editora_Constructor_ThrowsOnEmptyNome()
    {
        Assert.Throws<ArgumentException>(() => new Editora("", "slug"));
    }

    [Fact]
    public void Editora_Constructor_ThrowsOnEmptySlug()
    {
        Assert.Throws<ArgumentException>(() => new Editora("Nome", ""));
    }

    [Fact]
    public void Editora_AtualizarBranding_UpdatesBranding()
    {
        var editora = new Editora("Editora", "editora");
        editora.AtualizarBranding("https://logo.url", "#FF0000");
        Assert.Equal("https://logo.url", editora.LogoUrl);
        Assert.Equal("#FF0000", editora.CorPrimaria);
    }

    [Fact]
    public void Editora_AtualizarBranding_SetsNullValues()
    {
        var editora = new Editora("Editora", "editora");
        editora.AtualizarBranding(null, null);
        Assert.Null(editora.LogoUrl);
        Assert.Null(editora.CorPrimaria);
    }

    [Fact]
    public void Editora_Desativar_SetsAtivoFalse()
    {
        var editora = new Editora("Editora", "editora");
        editora.Desativar();
        Assert.False(editora.Ativo);
    }

    [Fact]
    public void Editora_Ativar_SetsAtivoTrue()
    {
        var editora = new Editora("Editora", "editora");
        editora.Desativar();
        editora.Ativar();
        Assert.True(editora.Ativo);
    }

    [Fact]
    public void Editora_Usuarios_IsInitiallyEmpty()
    {
        var editora = new Editora("Editora", "editora");
        Assert.NotNull(editora.Usuarios);
        Assert.Empty(editora.Usuarios);
    }
}
