using Companella.Components.Misc;
using Companella.Models.Difficulty;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Tools;

/// <summary>
/// A triple-toggle for selecting BPM factor (0.5x, 1x, 2x).
/// </summary>
public partial class BpmFactorToggle : CompositeDrawable
{
	private BpmFactor _currentFactor = BpmFactor.Normal;
	private StyledButton _halfOption = null!;
	private StyledButton _normalOption = null!;
	private StyledButton _doubleOption = null!;
	private bool _isEnabled = true;

	public event Action<BpmFactor>? FactorChanged;

	/// <summary>
	/// Gets or sets the current BPM factor.
	/// </summary>
	public BpmFactor CurrentFactor
	{
		get => _currentFactor;
		set
		{
			if (_currentFactor == value)
				return;
			_currentFactor = value;
			UpdateSelection();
			FactorChanged?.Invoke(_currentFactor);
		}
	}

	/// <summary>
	/// Gets or sets whether the toggle is enabled.
	/// </summary>
	public bool Enabled
	{
		get => _isEnabled;
		set
		{
			_isEnabled = value;
			if (_halfOption != null)
			{
				_halfOption.Enabled = value;
				_normalOption.Enabled = value;
				_doubleOption.Enabled = value;
			}
		}
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		Masking = true;
		CornerRadius = 2;

		InternalChildren = new Drawable[]
		{
			new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = StyledButton.Theme.DisabledBg
			},
			new FillFlowContainer
			{
				RelativeSizeAxes = Axes.Both,
				Direction = FillDirection.Horizontal,
				Spacing = new Vector2(2, 0),
				Padding = new MarginPadding(2),
				Children = new Drawable[]
				{
					_halfOption = CreateOption(BpmFactor.Half, "Halve detected BPM (use if detection doubled the BPM)"),
					_normalOption = CreateOption(BpmFactor.Normal, "Use detected BPM as-is"),
					_doubleOption = CreateOption(BpmFactor.Double, "Double detected BPM (use if detection halved the BPM)")
				}
			}
		};

		UpdateSelection();
	}

	private StyledButton CreateOption(BpmFactor factor, string tooltip)
	{
		var button = new StyledButton(factor.GetLabel(), StyledButtonAppearance.Toggle)
		{
			RelativeSizeAxes = Axes.Y,
			Width = 40,
			FontSize = 16,
			SuppressClickWhenSelected = true,
			Tag = factor,
			TooltipText = tooltip
		};
		button.Clicked += () => OnOptionSelected(factor);
		return button;
	}

	private void OnOptionSelected(BpmFactor factor)
	{
		if (!_isEnabled)
			return;

		CurrentFactor = factor;
	}

	private void UpdateSelection()
	{
		if (_halfOption == null)
			return;

		_halfOption.SetSelected(_currentFactor == BpmFactor.Half);
		_normalOption.SetSelected(_currentFactor == BpmFactor.Normal);
		_doubleOption.SetSelected(_currentFactor == BpmFactor.Double);
	}
}
