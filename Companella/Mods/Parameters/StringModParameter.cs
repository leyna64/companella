namespace Companella.Mods.Parameters;

/// <summary>
/// String parameter that allows text input.
/// </summary>
public class StringModParameter : IModParameter
{
	private string _value;

	/// <summary>
	/// Display name shown in the UI.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// Description shown as tooltip.
	/// </summary>
	public string Description { get; }

	/// <summary>
	/// The underlying type is string.
	/// </summary>
	public Type ParameterType => typeof(string);

	/// <summary>
	/// Current value of the parameter.
	/// </summary>
	public string Value
	{
		get => _value;
		set => _value = value ?? "";
	}

	/// <summary>
	/// Default value for reset.
	/// </summary>
	public string DefaultValue { get; }

	/// <summary>
	/// String parameters are discrete.
	/// </summary>
	public bool IsDiscrete => true;

	/// <summary>
	/// Creates a new string parameter.
	/// </summary>
	/// <param name="name">Display name.</param>
	/// <param name="description">Tooltip description.</param>
	/// <param name="defaultValue">Default and initial value.</param>
	public StringModParameter(
		string name,
		string description,
		string defaultValue)
	{
		Name = name;
		Description = description;
		DefaultValue = defaultValue ?? "";
		_value = DefaultValue;
	}

	/// <inheritdoc />
	public object GetValue()
	{
		return Value;
	}

	/// <inheritdoc />
	public void SetValue(object value)
	{
		if (value is string strValue)
			Value = strValue;
		else
			Value = value?.ToString() ?? "";
	}

	/// <inheritdoc />
	public void Reset()
	{
		Value = DefaultValue;
	}

	/// <inheritdoc />
	public object GetDefaultValue()
	{
		return DefaultValue;
	}

	/// <inheritdoc />
	public object? GetMinValue()
	{
		return null;
	}

	/// <inheritdoc />
	public object? GetMaxValue()
	{
		return null;
	}

	/// <inheritdoc />
	public object? GetStep()
	{
		return null;
	}

	/// <inheritdoc />
	public IReadOnlyList<(object Value, string DisplayName)>? GetEnumValues()
	{
		return null;
	}

	/// <inheritdoc />
	public double GetNormalizedValue()
	{
		return 0;
	}

	/// <inheritdoc />
	public void SetNormalizedValue(double normalized)
	{
		// String parameters don't support normalized values
	}

	/// <inheritdoc />
	public string GetDisplayValue()
	{
		return Value;
	}

	public override string ToString()
	{
		return $"{Name}: {GetDisplayValue()}";
	}
}
