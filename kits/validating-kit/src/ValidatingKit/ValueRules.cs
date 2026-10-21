namespace ValidatingKit;

/// <summary>Rules for <see cref="Guid"/> and enum fields.</summary>
public static class ValueRules
{
    /// <summary>Fails when the value is <see cref="Guid.Empty"/>.</summary>
    /// <param name="field">The field.</param>
    /// <param name="message">The failure message. Defaults to <c>Must not be empty.</c></param>
    public static Field<Guid> NotEmpty(this Field<Guid> field, string? message = null) =>
        field.HasFailed || field.Value != Guid.Empty ? field : field.Fail(message ?? Messages.NotEmpty);

    /// <summary>Fails when the value is <see langword="null"/> or <see cref="Guid.Empty"/>.</summary>
    /// <param name="field">The field.</param>
    /// <param name="message">The failure message. Defaults to <c>Must not be empty.</c></param>
    public static Field<Guid?> NotEmpty(this Field<Guid?> field, string? message = null) =>
        field.HasFailed || (field.Value is { } value && value != Guid.Empty) ? field : field.Fail(message ?? Messages.NotEmpty);

    /// <summary>
    /// Fails unless the value is one of <typeparamref name="TEnum"/>'s named values. A combination of
    /// <see cref="FlagsAttribute"/> flags is not a named value, so it fails.
    /// </summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <param name="field">The field.</param>
    /// <param name="message">The failure message. Defaults to <c>Must be one of the defined values.</c></param>
    public static Field<TEnum> IsInEnum<TEnum>(this Field<TEnum> field, string? message = null)
        where TEnum : struct, Enum =>
        field.HasFailed || Enum.IsDefined(field.Value) ? field : field.Fail(message ?? Messages.InEnum);

    /// <inheritdoc cref="IsInEnum{TEnum}(Field{TEnum}, string)"/>
    public static Field<TEnum?> IsInEnum<TEnum>(this Field<TEnum?> field, string? message = null)
        where TEnum : struct, Enum =>
        field.Skip || Enum.IsDefined(field.Value!.Value) ? field : field.Fail(message ?? Messages.InEnum);
}
