using Companella.Components.Settings;
using Companella.Components.Tools;
using Companella.Models.Difficulty;
using Companella.Components.Misc;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osuTK;

namespace Companella.Components.Misc;

/// <summary>
/// Panel containing function buttons.
/// </summary>
public partial class FunctionButtonPanel : CompositeDrawable
{
	private StyledButton _analyzeBpmButton = null!;
	private StyledButton _normalizeSvButton = null!;
	private BpmFactorToggle _bpmFactorToggle = null!;

	public event Action? AnalyzeBpmClicked;
	public event Action? NormalizeSvClicked;

	public BpmFactor SelectedBpmFactor => _bpmFactorToggle?.CurrentFactor ?? BpmFactor.Normal;

	public FunctionButtonPanel()
	{
		RelativeSizeAxes = Axes.X;
		AutoSizeAxes = Axes.Y;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		var content = new FillFlowContainer
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Direction = FillDirection.Vertical,
			Spacing = new Vector2(0, 10),
			Children = new Drawable[]
			{
				SettingsLayout.CreateHint("Detect BPM from audio or normalize scroll velocity for variable-BPM maps."),
				new FillFlowContainer
				{
					RelativeSizeAxes = Axes.X,
					AutoSizeAxes = Axes.Y,
					Direction = FillDirection.Vertical,
					Spacing = new Vector2(0, 8),
					Children = new Drawable[]
					{
						_analyzeBpmButton = new StyledButton("BPM (BETA)")
						{
							RelativeSizeAxes = Axes.X,
							Height = 32,
							Enabled = false,
							TooltipText = "Detect BPM from audio and generate timing points"
						},
						_bpmFactorToggle = new BpmFactorToggle
						{
							RelativeSizeAxes = Axes.X,
							Height = 32
						},
						_normalizeSvButton = new StyledButton("Normalize SV")
						{
							RelativeSizeAxes = Axes.X,
							Height = 32,
							Enabled = false,
							TooltipText = "Convert variable BPM to constant BPM with SV compensation"
						}
					}
				}
			}
		};

		InternalChild = SettingsLayout.CreateSection("BPM Analysis", content);

		_analyzeBpmButton.Clicked += () => AnalyzeBpmClicked?.Invoke();
		_normalizeSvButton.Clicked += () => NormalizeSvClicked?.Invoke();
	}

	public void SetEnabled(bool enabled)
	{
		_analyzeBpmButton.Enabled = enabled;
		_normalizeSvButton.Enabled = enabled;
		_bpmFactorToggle.Enabled = enabled;
	}
}
