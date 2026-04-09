using DomoLibri.Domain.Attributes;
using DomoLibri.Domain.ValueObjects;

namespace DomoLibri.Domain.Entities;

/// <summary>
/// Global identity entity. One record per unique email, shared across all Editoras.
/// Authentication credentials live here; per-tenant membership is in <see cref="VinculoUsuarioEditora"/>.
/// </summary>
public class Usuario
{
    protected Usuario() { }

    public Usuario(string email, string senhaHash, string nome)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("E-mail é obrigatório.", nameof(email));
        if (string.IsNullOrWhiteSpace(senhaHash)) throw new ArgumentException("Senha hash é obrigatória.", nameof(senhaHash));
        if (string.IsNullOrWhiteSpace(nome)) throw new ArgumentException("Nome é obrigatório.", nameof(nome));

        Id = Guid.NewGuid();
        Email = new Email(email);
        SenhaHash = senhaHash;
        Nome = nome.Trim();
        EmailConfirmado = false;
    }

    public Guid Id { get; private set; }
    public Email Email { get; private set; } = null!;
    [SensitiveData] public string SenhaHash { get; private set; } = string.Empty;
    public string Nome { get; private set; } = string.Empty;
    public bool EmailConfirmado { get; private set; }
    [SensitiveData] public string? TokenConfirmacao { get; private set; }
    public DateTime? ExpiracaoToken { get; private set; }
    [SensitiveData] public string? TokenRedefinicaoSenha { get; private set; }
    public DateTime? ExpiracaoTokenRedefinicaoSenha { get; private set; }
    public DateTime? SenhaAlteradaEm { get; private set; }
    public int AcessosFalhos { get; private set; }
    public DateTime? BloqueioAte { get; private set; }
    public ICollection<VinculoUsuarioEditora> Vinculos { get; private set; } = new List<VinculoUsuarioEditora>();

    public void DefinirTokenConfirmacao(string tokenHash, DateTime expiracao)
    {
        TokenConfirmacao = tokenHash;
        ExpiracaoToken = expiracao;
    }

    public void ConfirmarEmail()
    {
        if (EmailConfirmado) throw new InvalidOperationException("E-mail já confirmado.");
        EmailConfirmado = true;
        TokenConfirmacao = null;
        ExpiracaoToken = null;
    }

    public void DefinirTokenRedefinicaoSenha(string tokenHash, DateTime expiracao)
    {
        TokenRedefinicaoSenha = tokenHash;
        ExpiracaoTokenRedefinicaoSenha = expiracao;
    }

    public void RedefinirSenha(string novaSenhaHash)
    {
        if (string.IsNullOrWhiteSpace(novaSenhaHash)) throw new ArgumentException("Hash da nova senha é obrigatório.", nameof(novaSenhaHash));
        SenhaHash = novaSenhaHash;
        SenhaAlteradaEm = DateTime.UtcNow;
        TokenRedefinicaoSenha = null;
        ExpiracaoTokenRedefinicaoSenha = null;
    }

    public void RegistrarAcessoFalho(int maxTentativas = 5, int minutosBloqueio = 15)
    {
        AcessosFalhos++;
        if (AcessosFalhos >= maxTentativas)
        {
            BloqueioAte = DateTime.UtcNow.AddMinutes(minutosBloqueio);
            AcessosFalhos = 0;
        }
    }

    public void ResetarBloqueio()
    {
        AcessosFalhos = 0;
        BloqueioAte = null;
    }

    public bool EstaBloqueado() => BloqueioAte.HasValue && BloqueioAte.Value > DateTime.UtcNow;
}
