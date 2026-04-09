namespace DomoLibri.Application.Services;

public record EditoraBrandingResult(
    string? NomeEditora,
    string? LogoUrl,
    string? CorPrimaria,
    bool BrandingConfigurado);

public interface IEditoraQueryService
{
    Task<EditoraBrandingResult?> GetBrandingAsync(Guid editoraId);
}
