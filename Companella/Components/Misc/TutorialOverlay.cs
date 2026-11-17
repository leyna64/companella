using Companella.Models.Application;
using Companella.Services.Common;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Misc;

/// <summary>
/// An interactive tutorial overlay that guides new users through the application.
/// </summary>
public partial class TutorialOverlay : CompositeDrawable
{
	private Container _dialogContainer = null!;
	private SpriteText _titleText = null!;
	private TextFlowContainer _descriptionText = null!;
	private SpriteText _progressText = null!;
	private StyledButton _previousButton = null!;
	private StyledButton _nextButton = null!;
	private StyledButton _skipButton = null!;
	private StyledButton _quickSetupButton = null!;

	private TutorialService _tutorialService = null!;

	/// <summary>
	/// Event raised when the tutorial is completed or skipped.
	/// </summary>
	public event Action? TutorialCompleted;

	/// <summary>
	/// Event raised when a main tab switch is required.
	/// </summary>
	public event Action<int>? MainTabSwitchRequested;

	/// <summary>
	/// Event raised when a split tab switch is required within a main tab.
	/// </summary>
	public event Action<int, int>? SplitTabSwitchRequested;

	/// <summary>
	/// Event raised when the Quick Setup button is clicked.
	/// </summary>
	public event Action? QuickSetupRequested;

	public TutorialOverlay()
	{
		RelativeSizeAxes = Axes.Both;
		Alpha = 0;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		_tutorialService = new TutorialService();
		_tutorialService.MainTabSwitchRequested += OnMainTabSwitchRequested;
		_tutorialService.SplitTabSwitchRequested += OnSplitTabSwitchRequested;

		InternalChildren = new Drawable[]
		{
			StyledDialog.CreateDimBackground(),
			_dialogContainer = StyledDialog.CreateShell(new Vector2(520, 300), out var content)
		};

		content.Children = new Drawable[]
		{
			new FillFlowContainer
			{
				RelativeSizeAxes = Axes.X,
				AutoSizeAxes = Axes.Y,
				Direction = FillDirection.Vertical,
				Spacing = new Vector2(0, 8),
				Children = new Drawable[]
				{
					_progressText = new SpriteText
					{
						Anchor = Anchor.TopCentre,
						Origin = Anchor.TopCentre,
						Text = "Step 1 of 12",
						Font = new FontUsage("", 13),
						Colour = StyledButton.Theme.DisabledLabel
					},
					_titleText = StyledDialog.CreateTitle("Welcome to Companella!")
				}
			},
			_descriptionText = new TextFlowContainer(s =>
			{
				s.Font = new FontUsage("", 15);
				s.Colour = StyledButton.Theme.MutedLabel;
			})
			{
				Anchor = Anchor.TopCentre,
				Origin = Anchor.TopCentre,
				RelativeSizeAxes = Axes.X,
				AutoSizeAxes = Axes.Y,
				Y = 70,
				TextAnchor = Anchor.TopLeft,
				Text = "Welcome!"
			},
			new FillFlowContainer
			{
				Anchor = Anchor.BottomCentre,
				Origin = Anchor.BottomCentre,
				AutoSizeAxes = Axes.Both,
				Direction = FillDirection.Horizontal,
				Spacing = new Vector2(10, 0),
				Children = new Drawable[]
				{
					_skipButton = new StyledButton("Skip", StyledButtonAppearance.Muted) { Size = new Vector2(90, 36) },
					_previousButton = new StyledButton("Previous", StyledButtonAppearance.Muted) { Size = new Vector2(100, 36) },
					_quickSetupButton = new StyledButton("Quick Setup") { AccentColor = StyledButton.Theme.SuccessFill, Size = new Vector2(120, 36), Alpha = 0 },
					_nextButton = new StyledButton("Next") { Size = new Vector2(100, 36) }
				}
			}
		};

		_skipButton.Clicked += OnSkipClicked;
		_previousButton.Clicked += OnPreviousClicked;
		_nextButton.Clicked += OnNextClicked;
		_quickSetupButton.Clicked += OnQuickSetupClicked;
	}

	/// <summary>
	/// Shows the tutorial overlay, starting from the first step.
	/// </summary>
	public new void Show()
	{
		_tutorialService.Reset();
		UpdateCurrentStep();

		this.FadeIn(300, Easing.OutQuint);
		_dialogContainer.ScaleTo(0.96f).ScaleTo(1f, 300, Easing.OutQuint);
	}

	/// <summary>
	/// Hides the tutorial overlay.
	/// </summary>
	public new void Hide()
	{
		StyledDialog.PlayHideAnimation(this);
	}

	private void UpdateCurrentStep()
	{
		var step = _tutorialService.CurrentStep;
		if (step == null)
			return;

		_progressText.Text = $"Step {_tutorialService.CurrentStepIndex + 1} of {_tutorialService.TotalSteps}";
		_titleText.Text = step.Title;
		_descriptionText.Text = step.Description;

		_previousButton.Alpha = _tutorialService.HasPreviousStep ? 1f : 0.5f;
		_previousButton.Enabled = _tutorialService.HasPreviousStep;

		if (step.ShowQuickSetup)
		{
			_quickSetupButton.FadeIn(200);
			_nextButton.Text = "Skip Setup";
			_skipButton.Alpha = 0;
		}
		else
		{
			_quickSetupButton.FadeOut(200);
			_nextButton.Text = _tutorialService.IsLastStep ? "Finish" : "Next";
			_skipButton.Alpha = 1;
		}

		var estimatedHeight = 300f;
		if (step.Description.Length > 200)
			estimatedHeight = 360f;
		if (step.Description.Length > 350)
			estimatedHeight = 420f;
		if (step.ShowQuickSetup)
			estimatedHeight = 400f;

		_dialogContainer.ResizeHeightTo(estimatedHeight, 200, Easing.OutQuint);
		PositionDialog(step.DialogPosition);
	}

	private void PositionDialog(TutorialDialogPosition position)
	{
		Vector2 targetPosition = position switch
		{
			TutorialDialogPosition.Top => new Vector2(0, -180),
			TutorialDialogPosition.Bottom => new Vector2(0, 150),
			TutorialDialogPosition.BottomRight => new Vector2(80, 120),
			_ => Vector2.Zero
		};

		_dialogContainer.MoveTo(targetPosition, 250, Easing.OutQuint);
	}

	private void OnSkipClicked()
	{
		Hide();
		TutorialCompleted?.Invoke();
	}

	private void OnPreviousClicked()
	{
		if (_tutorialService.PreviousStep())
			UpdateCurrentStep();
	}

	private void OnNextClicked()
	{
		if (_tutorialService.IsLastStep)
		{
			Hide();
			TutorialCompleted?.Invoke();
		}
		else if (_tutorialService.NextStep())
		{
			UpdateCurrentStep();
		}
	}

	private void OnQuickSetupClicked()
	{
		Hide();
		QuickSetupRequested?.Invoke();
		TutorialCompleted?.Invoke();
	}

	private void OnMainTabSwitchRequested(int tabIndex) => MainTabSwitchRequested?.Invoke(tabIndex);

	private void OnSplitTabSwitchRequested(int mainTabIndex, int splitTabIndex) =>
		SplitTabSwitchRequested?.Invoke(mainTabIndex, splitTabIndex);

	protected override bool OnClick(ClickEvent e) => true;
}
