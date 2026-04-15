using System.Security.Cryptography;
using System.Text.Json;
using DomoLibri.Application.Services;
using DomoLibri.Domain.Entities;
using DomoLibri.Domain.Enums;
using DomoLibri.Domain.Interfaces;
using DomoLibri.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DomoLibri.Infrastructure.Services;

public class UsuarioService : IUsuarioService
{
    private readonly DomoLibriDbContext _context;
    private readonly IUsuarioRepository _usuarioRepository;
    private readonly IUserContextProvider _userContextProvider;

    public UsuarioService(
        DomoLibriDbContext context,
        IUsuarioRepository usuarioRepository,
        IUserContextProvider userContextProvider)
    {
        _context = context;
        _usuarioRepository = usuarioRepository;
        _userContextProvider = userContextProvider;
    }

    public async Task<IReadOnlyList<VinculoUsuarioResponseDto>> ListarUsuariosAsync(Guid currentEditoraId)
    {
        if (currentEditoraId == Guid.Empty)
            throw new InvalidOperationException("Editora atual não informada.");

        return await _context.VinculosUsuarioEditora
            .Include(v => v.Usuario)
            .Where(v => v.EditoraId == currentEditoraId)
            .OrderBy(v => v.Usuario!.Nome)
            .Select(v => new VinculoUsuarioResponseDto(
                v.Id,
                v.Usuario!.Nome,
                v.Usuario.Email.Value,
                FormatTipoVinculo(v.TipoVinculo),
                v.Ativo ? "Ativo" : "Inativo",
                v.DataEntrada))
            .ToListAsync();
    }

    public async Task<VinculoUsuarioResponseDto> CadastrarOuVincularUsuarioAsync(CriarUsuarioDto dto, Guid currentEditoraId)
    {
        if (currentEditoraId == Guid.Empty)
            throw new InvalidOperationException("Editora atual não informada.");

        var actorId = _userContextProvider.GetUserId()
            ?? throw new InvalidOperationException("Usuário autenticado não identificado.");

        var nome = dto.Nome.Trim();
        var email = dto.Email.Trim().ToLowerInvariant();
        var tipoVinculo = ParseTipoVinculo(dto.TipoVinculo);

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var usuario = await _usuarioRepository.FindByEmailAsync(email);
        var criouNovoUsuario = usuario is null;

        if (usuario is null)
        {
            usuario = new Usuario(email, BCrypt.Net.BCrypt.HashPassword(GeneratePlaceholderPassword()), nome);
            await _usuarioRepository.AddAsync(usuario);
        }

        var vinculoExistente = await _context.VinculosUsuarioEditora
            .IgnoreQueryFilters()
            .AnyAsync(v => v.UsuarioId == usuario.Id && v.EditoraId == currentEditoraId);

        if (vinculoExistente)
            throw new InvalidOperationException("Usuário já possui vínculo com esta editora.");

        var vinculo = VinculoUsuarioEditora.Criar(usuario.Id, currentEditoraId, tipoVinculo);
        _context.VinculosUsuarioEditora.Add(vinculo);

        _context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            EditoraId = currentEditoraId,
            UsuarioId = actorId,
            Acao = criouNovoUsuario ? "UsuarioCriadoEVinculado" : "UsuarioVinculado",
            Recurso = "VinculoUsuarioEditora",
            RecursoId = vinculo.Id.ToString(),
            IP = _userContextProvider.GetIp() ?? string.Empty,
            UserAgent = _userContextProvider.GetUserAgent() ?? string.Empty,
            DataHora = DateTime.UtcNow,
            DadosNovos = JsonSerializer.Serialize(new
            {
                vinculo.Id,
                vinculo.UsuarioId,
                vinculo.EditoraId,
                Nome = usuario.Nome,
                Email = usuario.Email.Value,
                TipoVinculo = FormatTipoVinculo(vinculo.TipoVinculo),
                Status = vinculo.ObterStatus()
            })
        });

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return ToResponse(vinculo, usuario);
    }

    private static VinculoUsuarioResponseDto ToResponse(VinculoUsuarioEditora vinculo, Usuario? usuario = null)
    {
        var resolvedUser = usuario ?? vinculo.Usuario ?? throw new InvalidOperationException("Usuário do vínculo não carregado.");
        return new VinculoUsuarioResponseDto(
            vinculo.Id,
            resolvedUser.Nome,
            resolvedUser.Email.Value,
            FormatTipoVinculo(vinculo.TipoVinculo),
            vinculo.ObterStatus(),
            vinculo.DataEntrada);
    }

    private static TipoVinculo ParseTipoVinculo(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Tipo de vínculo é obrigatório.");

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "administrador" => TipoVinculo.Administrador,
            "colaborador" => TipoVinculo.Colaborador,
            "convidado" => TipoVinculo.Convidado,
            "interno" => TipoVinculo.Interno,
            "freelancer" => TipoVinculo.Freelancer,
            "autor" => TipoVinculo.Autor,
            "parceiro" => TipoVinculo.Parceiro,
            "sistema" => TipoVinculo.Sistema,
            _ => throw new InvalidOperationException("Tipo de vínculo inválido.")
        };
    }

    private static string FormatTipoVinculo(TipoVinculo tipoVinculo)
        => tipoVinculo switch
        {
            TipoVinculo.Administrador => "Administrador",
            TipoVinculo.Colaborador => "Colaborador",
            TipoVinculo.Convidado => "Convidado",
            TipoVinculo.Interno => "Interno",
            TipoVinculo.Freelancer => "Freelancer",
            TipoVinculo.Autor => "Autor",
            TipoVinculo.Parceiro => "Parceiro",
            TipoVinculo.Sistema => "Sistema",
            _ => tipoVinculo.ToString()
        };

    private static string GeneratePlaceholderPassword()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
}
