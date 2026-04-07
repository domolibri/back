namespace DomoLibri.Domain.Attributes;

/// <summary>
/// Marks a property as sensitive. The <c>AuditInterceptor</c> will exclude properties
/// carrying this attribute from <c>DadosOriginais</c> and <c>DadosNovos</c> in audit logs,
/// preventing accidental exposure of passwords, tokens, and other PII (LGPD compliance).
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class SensitiveDataAttribute : Attribute;
