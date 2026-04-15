using DomoLibri.Domain.Entities;
using DomoLibri.Domain.Enums;

namespace DomoLibri.Tests.Domain;

public class VinculoUsuarioEditoraTests
{
    [Fact]
    public void VinculoUsuarioEditora_DefaultValues_AreCorrect()
    {
        var vinculo = new VinculoUsuarioEditora();

        Assert.Equal(Guid.Empty, vinculo.Id);
        Assert.Equal(Guid.Empty, vinculo.EditoraId);
        Assert.Equal(Guid.Empty, vinculo.UsuarioId);
        Assert.False(vinculo.Ativo);
        Assert.Equal(TipoVinculo.Colaborador, vinculo.TipoVinculo);
        Assert.Null(vinculo.Usuario);
        Assert.Null(vinculo.Editora);
        Assert.NotNull(vinculo.Roles);
        Assert.Empty(vinculo.Roles);
    }

    [Fact]
    public void VinculoUsuarioEditora_PropertiesCanBeSet()
    {
        var id = Guid.NewGuid();
        var editoraId = Guid.NewGuid();
        var usuarioId = Guid.NewGuid();
        var dataEntrada = DateTime.UtcNow;

        var vinculo = new VinculoUsuarioEditora
        {
            Id = id,
            EditoraId = editoraId,
            UsuarioId = usuarioId,
            Ativo = true,
            DataEntrada = dataEntrada,
            TipoVinculo = TipoVinculo.Administrador
        };

        Assert.Equal(id, vinculo.Id);
        Assert.Equal(editoraId, vinculo.EditoraId);
        Assert.Equal(usuarioId, vinculo.UsuarioId);
        Assert.True(vinculo.Ativo);
        Assert.Equal(dataEntrada, vinculo.DataEntrada);
        Assert.Equal(TipoVinculo.Administrador, vinculo.TipoVinculo);
    }

    [Fact]
    public void VinculoUsuarioEditora_Criar_DefinesExpectedDefaults()
    {
        var usuarioId = Guid.NewGuid();
        var editoraId = Guid.NewGuid();

        var vinculo = VinculoUsuarioEditora.Criar(usuarioId, editoraId, TipoVinculo.Parceiro);

        Assert.NotEqual(Guid.Empty, vinculo.Id);
        Assert.Equal(usuarioId, vinculo.UsuarioId);
        Assert.Equal(editoraId, vinculo.EditoraId);
        Assert.True(vinculo.Ativo);
        Assert.Equal(TipoVinculo.Parceiro, vinculo.TipoVinculo);
        Assert.Equal("Ativo", vinculo.ObterStatus());
    }

    [Fact]
    public void VinculoUsuarioEditora_NavigationProperties_CanBeSet()
    {
        var editora = new Editora("Editora", "editora");
        var usuario = new Usuario("test@test.com", "hash", "Teste");

        var vinculo = new VinculoUsuarioEditora
        {
            Editora = editora,
            Usuario = usuario
        };

        Assert.NotNull(vinculo.Editora);
        Assert.Equal(editora.Id, vinculo.Editora.Id);
        Assert.NotNull(vinculo.Usuario);
        Assert.Equal(usuario.Id, vinculo.Usuario.Id);
    }

    [Fact]
    public void VinculoUsuarioEditora_TipoVinculo_HasCorrectValues()
    {
        Assert.Equal(0, (int)TipoVinculo.Administrador);
        Assert.Equal(1, (int)TipoVinculo.Colaborador);
        Assert.Equal(2, (int)TipoVinculo.Convidado);
        Assert.Equal(3, (int)TipoVinculo.Interno);
        Assert.Equal(6, (int)TipoVinculo.Parceiro);
    }
}
