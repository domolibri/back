using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DomoLibri.Domain.ValueObjects;

[JsonConverter(typeof(EmailJsonConverter))]
public sealed class Email : ValueObject
{
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string Value { get; }

    public Email(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("E-mail não pode ser vazio.", nameof(value));

        var normalized = value.Trim().ToLower();

        if (!EmailRegex.IsMatch(normalized))
            throw new ArgumentException($"'{value}' não é um endereço de e-mail válido.", nameof(value));

        Value = normalized;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    public static implicit operator string(Email email) => email.Value;
    public static explicit operator Email(string value) => new(value);
}

public class EmailJsonConverter : JsonConverter<Email>
{
    public override Email Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetString()!);

    public override void Write(Utf8JsonWriter writer, Email value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}
