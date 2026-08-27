using System.ComponentModel.DataAnnotations;

namespace Slypn.Api.Models.Inputs;

/// <summary>
/// Caps the length of each string in a collection. DataAnnotations validates the property,
/// not its elements, so [StringLength] on a List&lt;string&gt; checks nothing — this fills that
/// gap for free-text lists that would otherwise be unbounded per item.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class ItemLengthAttribute(int maximumLength) : ValidationAttribute
{
    public int MaximumLength { get; } = maximumLength;

    public override bool IsValid(object? value)
    {
        if (value is not IEnumerable<string?> items) return true; // wrong shape is another rule's problem
        return items.All(item => (item?.Length ?? 0) <= MaximumLength);
    }

    public override string FormatErrorMessage(string name)
        => $"Each entry in {name} must be {MaximumLength} characters or fewer.";
}
