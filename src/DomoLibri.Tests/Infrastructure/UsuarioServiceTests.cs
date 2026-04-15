using DomoLibri.Application.Services;
using DomoLibri.Domain.Entities;
using DomoLibri.Domain.Enums;
using DomoLibri.Domain.Interfaces;
using DomoLibri.Infrastructure.Data;
using DomoLibri.Infrastructure.Data.Repositories;
using DomoLibri.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace DomoLibri.Tests.Infrastructure;

public class UsuarioServiceTests
{
    private record SutContext(
        DomoLibriDbContext Db,
        UsuarioService Sut,
        Guid TenantId,
        Guid ActorId);

    private static SutContext CreateSut(Guid? tenantIdOverride = null, Guid? actorIdOverride = null)
    {
        var tenantId = tenantIdOverride ?? Guid.NewGuid();
        var actorId = actorIdOverride ?? Guid.NewGuid();

        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(t => t.GetTenantId()).Returns(tenantId);

        var userContext = new Mock<IUserContextProvider>();
        userContext.Setup(u => u.GetUserId()).Returns(actorId);
        userContext.Setup(u => u.GetIp()).Returns("127.0.0.1");
        userContext.Setup(u => u.GetUserAgent()).Returns("Tests/1.0");

        var options = new DbContextOptionsBuilder<DomoLibriDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var db = new DomoLibriDbContext(options, tenantProvider.Object);
        var repository = new UsuarioRepository(db);
        var sut = new UsuarioService(db, repository, userContext.Object);
        return new SutContext(db, sut, tenantId, actorId);
    }

    [Fact]
    public async Task CadastrarOuVincularUsuarioAsync_CreatesGlobalUserAndTenantBinding()
    {
        var ctx = CreateSut();
        ctx.Db.Editoras.Add(new Editora("Editora Teste", "editora-teste"));
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Sut.CadastrarOuVincularUsuarioAsync(
            new CriarUsuarioDto("Maria", "maria@editora.com", "Interno"),
            ctx.TenantId);

        var usuario = await ctx.Db.Usuarios.SingleAsync();
        var vinculo = await ctx.Db.VinculosUsuarioEditora.SingleAsync();
        var audit = await ctx.Db.AuditLogs.IgnoreQueryFilters().SingleAsync();

        Assert.Equal(usuario.Id, vinculo.UsuarioId);
        Assert.Equal("Maria", result.Nome);
        Assert.Equal("maria@editora.com", result.Email);
        Assert.Equal("Interno", result.TipoVinculo);
        Assert.Equal("Ativo", result.Status);
        Assert.Equal(ctx.TenantId, vinculo.EditoraId);
        Assert.Equal(TipoVinculo.Interno, vinculo.TipoVinculo);
        Assert.Equal("UsuarioCriadoEVinculado", audit.Acao);
        Assert.Equal(ctx.ActorId, audit.UsuarioId);
    }

    [Fact]
    public async Task CadastrarOuVincularUsuarioAsync_ReusesExistingGlobalUser()
    {
        var ctx = CreateSut();
        var editora = new Editora("Editora Teste", "editora-teste");
        var outroTenant = Guid.NewGuid();
        var usuario = new Usuario("compartilhado@editora.com", "hash", "Compartilhado");
        ctx.Db.Editoras.Add(editora);
        ctx.Db.Usuarios.Add(usuario);
        ctx.Db.VinculosUsuarioEditora.Add(VinculoUsuarioEditora.Criar(usuario.Id, outroTenant, TipoVinculo.Parceiro));
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Sut.CadastrarOuVincularUsuarioAsync(
            new CriarUsuarioDto("Nome Ignorado", "compartilhado@editora.com", "Parceiro"),
            ctx.TenantId);

        Assert.Equal(1, await ctx.Db.Usuarios.CountAsync());
        Assert.Equal(2, await ctx.Db.VinculosUsuarioEditora.IgnoreQueryFilters().CountAsync());
        Assert.Equal("Compartilhado", result.Nome);
        Assert.Equal("Parceiro", result.TipoVinculo);
    }

    [Fact]
    public async Task CadastrarOuVincularUsuarioAsync_WhenBindingAlreadyExists_ThrowsConflict()
    {
        var ctx = CreateSut();
        var editora = new Editora("Editora Teste", "editora-teste");
        var usuario = new Usuario("duplicado@editora.com", "hash", "Duplicado");
        ctx.Db.Editoras.Add(editora);
        ctx.Db.Usuarios.Add(usuario);
        ctx.Db.VinculosUsuarioEditora.Add(VinculoUsuarioEditora.Criar(usuario.Id, ctx.TenantId, TipoVinculo.Interno));
        await ctx.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ctx.Sut.CadastrarOuVincularUsuarioAsync(
                new CriarUsuarioDto("Duplicado", "duplicado@editora.com", "Interno"),
                ctx.TenantId));

        Assert.Equal("Usuário já possui vínculo com esta editora.", ex.Message);
    }

    [Fact]
    public async Task ListarUsuariosAsync_ReturnsOnlyCurrentTenantBindings()
    {
        var ctx = CreateSut();
        var editora = new Editora("Editora Teste", "editora-teste");
        var outroTenant = Guid.NewGuid();
        var usuarioA = new Usuario("a@editora.com", "hash", "Ana");
        var usuarioB = new Usuario("b@editora.com", "hash", "Bruno");
        ctx.Db.Editoras.Add(editora);
        ctx.Db.Usuarios.AddRange(usuarioA, usuarioB);
        ctx.Db.VinculosUsuarioEditora.AddRange(
            VinculoUsuarioEditora.Criar(usuarioA.Id, ctx.TenantId, TipoVinculo.Interno),
            VinculoUsuarioEditora.Criar(usuarioB.Id, outroTenant, TipoVinculo.Parceiro));
        await ctx.Db.SaveChangesAsync();

        var usuarios = await ctx.Sut.ListarUsuariosAsync(ctx.TenantId);

        Assert.Single(usuarios);
        Assert.Equal("Ana", usuarios[0].Nome);
    }
}
