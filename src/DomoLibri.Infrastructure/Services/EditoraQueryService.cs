using DomoLibri.Application.Services;
using DomoLibri.Domain.Interfaces;

namespace DomoLibri.Infrastructure.Services;

public class EditoraQueryService : IEditoraQueryService
{
    private readonly IEditoraRepository _editoraRepository;

    public EditoraQueryService(IEditoraRepository editoraRepository)
    {
        _editoraRepository = editoraRepository;
    }

    public async Task<EditoraBrandingResult?> GetBrandingAsync(Guid editoraId)
    {
        var editora = await _editoraRepository.FindByIdAsync(editoraId);
        if (editora is null) return null;

        return new EditoraBrandingResult(
            editora.Nome,
            editora.LogoUrl,
            editora.CorPrimaria,
            editora.LogoUrl is not null || editora.CorPrimaria is not null);
    }
}
