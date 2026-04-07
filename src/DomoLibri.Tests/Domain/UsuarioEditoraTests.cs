using DomoLibri.Domain.Entities;

namespace DomoLibri.Tests.Domain;

public class UsuarioEditoraTests
{
    [Fact]
    public void UsuarioEditora_DefaultValues_AreCorrect()
    {
        var usuario = new UsuarioEditora();

        Assert.Equal(Guid.Empty, usuario.Id);
        Assert.Equal(Guid.Empty, usuario.EditoraId);
        Assert.Equal(string.Empty, usuario.Email);
        Assert.Equal(string.Empty, usuario.SenhaHash);
        Assert.Equal(string.Empty, usuario.Nome);
        Assert.False(usuario.Ativo);
        Assert.False(usuario.EmailConfirmado);
        Assert.Null(usuario.TokenConfirmacao);
        Assert.Null(usuario.ExpiracaoToken);
        Assert.Null(usuario.TokenRedefinicaoSenha);
        Assert.Null(usuario.ExpiracaoTokenRedefinicaoSenha);
        Assert.Equal(0, usuario.AcessosFalhos);
        Assert.Null(usuario.BloqueioAte);
        Assert.Null(usuario.Editora);
        Assert.NotNull(usuario.Roles);
        Assert.Empty(usuario.Roles);
    }

    [Fact]
    public void UsuarioEditora_PropertiesCanBeSet()
    {
        var id = Guid.NewGuid();
        var editoraId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var usuario = new UsuarioEditora
        {
            Id = id,
            EditoraId = editoraId,
            Email = "usuario@editora.com",
            SenhaHash = "hashed_password",
            Nome = "Usuário Teste",
            Ativo = true,
            EmailConfirmado = true,
            TokenConfirmacao = "abc123token",
            ExpiracaoToken = now.AddHours(24),
            TokenRedefinicaoSenha = "resettoken456",
            ExpiracaoTokenRedefinicaoSenha = now.AddHours(1),
            AcessosFalhos = 3,
            BloqueioAte = now.AddMinutes(15)
        };

        Assert.Equal(id, usuario.Id);
        Assert.Equal(editoraId, usuario.EditoraId);
        Assert.Equal("usuario@editora.com", usuario.Email);
        Assert.Equal("hashed_password", usuario.SenhaHash);
        Assert.Equal("Usuário Teste", usuario.Nome);
        Assert.True(usuario.Ativo);
        Assert.True(usuario.EmailConfirmado);
        Assert.Equal("abc123token", usuario.TokenConfirmacao);
        Assert.Equal(now.AddHours(24), usuario.ExpiracaoToken);
        Assert.Equal("resettoken456", usuario.TokenRedefinicaoSenha);
        Assert.Equal(now.AddHours(1), usuario.ExpiracaoTokenRedefinicaoSenha);
        Assert.Equal(3, usuario.AcessosFalhos);
        Assert.Equal(now.AddMinutes(15), usuario.BloqueioAte);
    }

    [Fact]
    public void UsuarioEditora_NavigationProperty_CanBeSet()
    {
        var editora = new Editora { Id = Guid.NewGuid(), Nome = "Editora" };
        var usuario = new UsuarioEditora { Editora = editora };

        Assert.NotNull(usuario.Editora);
        Assert.Equal(editora.Id, usuario.Editora.Id);
    }

    [Fact]
    public void UsuarioEditora_Lockout_TracksCorrectly()
    {
        var usuario = new UsuarioEditora();
        var bloqueioAte = DateTime.UtcNow.AddMinutes(15);

        usuario.AcessosFalhos = 5;
        usuario.BloqueioAte = bloqueioAte;

        Assert.Equal(5, usuario.AcessosFalhos);
        Assert.Equal(bloqueioAte, usuario.BloqueioAte);

        // Simula reset após desbloqueio
        usuario.AcessosFalhos = 0;
        usuario.BloqueioAte = null;

        Assert.Equal(0, usuario.AcessosFalhos);
        Assert.Null(usuario.BloqueioAte);
    }
}
