using Companella.Components.Misc;
using Companella.Components.Session;
using Companella.Services.Common;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;
using System.Globalization;

namespace Companella.Components.Settings;

/// <summary>
/// Panel for configuring replay analysis window settings.
/// </summary>
public partial class ReplayAnalysisSettingsPanel : CompositeDrawable
{
	private const int _defaultWidth = 800;
	private const int _defaultHeight = 400;
	private const int _defaultX = 100;
	private const int _defaultY = 100;

	[Resolved] private UserSettingsService SettingsService { get; set; } = null!;

	private SettingsCheckbox _enabledCheckbox = null!;
	private FillFlowContainer _optionsContainer = null!;
	private SpriteText _widthValueText = null!;
	private SpriteText _heightValueText = null!;
	private SpriteText _xValueText = null!;
	private SpriteText _yValueText = null!;

	[BackgroundDependencyLoader]
	private void load()
	{
		RelativeSizeAxes = Axes.X;
		AutoSizeAxes = Axes.Y;

		var settings = SettingsService.Settings;
		var sizeOptions = SettingsStepperOptions.ForReplaySize();
		var heightOptions = SettingsStepperOptions.ForReplayHeight();
		var positionOptions = SettingsStepperOptions.ForReplayPosition();

		var widthRow = SettingsLayout.CreateStepperRow(
			"Width", () => SettingsService.Settings.ReplayAnalysisWidth, SetWidth, out _widthValueText, sizeOptions);
		var heightRow = SettingsLayout.CreateStepperRow(
			"Height", () => SettingsService.Settings.ReplayAnalysisHeight, SetHeight, out _heightValueText, heightOptions);
		var xRow = SettingsLayout.CreateStepperRow(
			"X", () => SettingsService.Settings.ReplayAnalysisX, SetX, out _xValueText, positionOptions);
		var yRow = SettingsLayout.CreateStepperRow(
			"Y", () => SettingsService.Settings.ReplayAnalysisY, SetY, out _yValueText, positionOptions);

		var insetContent = new FillFlowContainer
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Direction = FillDirection.Vertical,
			Padding = new MarginPadding(12),
			Spacing = new Vector2(0, 10),
			Children = new Drawable[]
			{
				SettingsLayout.CreateHint("Hold Shift for larger adjustment steps."),
				SettingsLayout.CreateSubHeading("Size"),
				widthRow,
				heightRow,
				SettingsLayout.CreateSubHeading("Position"),
				xRow,
				yRow
			}
		};

		var insetSection = StyledDialog.CreateInsetSection();
		insetSection.Child = insetContent;

		_optionsContainer = new FillFlowContainer
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Direction = FillDirection.Vertical,
			Spacing = new Vector2(0, 10),
			Children = new Drawable[]
			{
				insetSection,
				new StyledButton("Reset", StyledButtonAppearance.Muted)
				{
					Width = 88,
					Height = 28,
					Action = OnResetClicked
				}
			}
		};

		var content = new FillFlowContainer
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Direction = FillDirection.Vertical,
			Spacing = new Vector2(0, 10),
			Children = new Drawable[]
			{
				_enabledCheckbox = new SettingsCheckbox
				{
					LabelText = "Show replay analysis on results screen",
					IsChecked = settings.ReplayAnalysisEnabled,
					TooltipText = "Show timing deviation chart after completing maps"
				},
				_optionsContainer
			}
		};

		InternalChild = new SettingsSection("Replay Analysis", "Timing deviation overlay shown after plays", content);

		_enabledCheckbox.CheckedChanged += OnEnabledChanged;
		UpdateOptionsVisibility(settings.ReplayAnalysisEnabled);
	}

	private void SetWidth(int value)
	{
		SettingsService.Settings.ReplayAnalysisWidth = value;
		SaveSettings();
	}

	private void SetHeight(int value)
	{
		SettingsService.Settings.ReplayAnalysisHeight = value;
		SaveSettings();
	}

	private void SetX(int value)
	{
		SettingsService.Settings.ReplayAnalysisX = value;
		SaveSettings();
	}

	private void SetY(int value)
	{
		SettingsService.Settings.ReplayAnalysisY = value;
		SaveSettings();
	}

	private void OnEnabledChanged(bool isChecked)
	{
		SettingsService.Settings.ReplayAnalysisEnabled = isChecked;
		UpdateOptionsVisibility(isChecked);
		SaveSettings();
	}

	private void UpdateOptionsVisibility(bool enabled) => _optionsContainer.Alpha = enabled ? 1f : 0.35f;

	private void OnResetClicked()
	{
		SettingsService.Settings.ReplayAnalysisWidth = _defaultWidth;
		SettingsService.Settings.ReplayAnalysisHeight = _defaultHeight;
		SettingsService.Settings.ReplayAnalysisX = _defaultX;
		SettingsService.Settings.ReplayAnalysisY = _defaultY;

		_widthValueText.Text = _defaultWidth.ToString(CultureInfo.InvariantCulture);
		_heightValueText.Text = _defaultHeight.ToString(CultureInfo.InvariantCulture);
		_xValueText.Text = _defaultX.ToString(CultureInfo.InvariantCulture);
		_yValueText.Text = _defaultY.ToString(CultureInfo.InvariantCulture);

		SaveSettings();
	}

	private void SaveSettings() => Task.Run(async () => await SettingsService.SaveAsync());
}
