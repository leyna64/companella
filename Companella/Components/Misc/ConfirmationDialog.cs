using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Misc;

/// <summary>
/// A simple confirmation dialog for yes/no actions.
/// </summary>
public partial class ConfirmationDialog : CompositeDrawable
{
	private Container _dialogContainer = null!;
	private SpriteText _titleText = null!;
	private TextFlowContainer _messageText = null!;
	private StyledButton _confirmButton = null!;
	private StyledButton _cancelButton = null!;
	private StyledButton? _skipButton;
	private FillFlowContainer _buttonContainer = null!;

	/// <summary>
	/// Event raised when the user confirms the action.
	/// </summary>
	public event Action? Confirmed;

	/// <summary>
	/// Event raised when the user skips the action (optional).
	/// </summary>
	public event Action? Skipped;

	/// <summary>
	/// Event raised when the dialog is closed (confirmed, skipped, or cancelled).
	/// </summary>
	public event Action? Closed;

	public ConfirmationDialog()
	{
		RelativeSizeAxes = Axes.Both;
		Alpha = 0;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		InternalChildren = new Drawable[]
		{
			StyledDialog.CreateDimBackground(),
			_dialogContainer = StyledDialog.CreateShell(new Vector2(480, 200), out var content)
		};

		content.Children = new Drawable[]
		{
			new FillFlowContainer
			{
				RelativeSizeAxes = Axes.X,
				AutoSizeAxes = Axes.Y,
				Direction = FillDirection.Vertical,
				Spacing = new Vector2(0, 16),
				Children = new Drawable[]
				{
					_titleText = StyledDialog.CreateTitle("Confirm Action"),
					_messageText = StyledDialog.CreateMessageFlow("Are you sure?")
				}
			},
			_buttonContainer = new FillFlowContainer
			{
				Anchor = Anchor.BottomCentre,
				Origin = Anchor.BottomCentre,
				AutoSizeAxes = Axes.Both,
				Direction = FillDirection.Horizontal,
				Spacing = new Vector2(10, 0),
				Children = new Drawable[]
				{
					_cancelButton = StyledDialog.CreateCancelButton(),
					_confirmButton = StyledDialog.CreatePrimaryButton("Confirm")
				}
			}
		};

		_cancelButton.Clicked += OnCancelClicked;
		_confirmButton.Clicked += OnConfirmClicked;
	}

	/// <summary>
	/// Shows the confirmation dialog with the specified title and message.
	/// </summary>
	public void Show(string title, string message, bool isDangerous = false, bool showSkip = false,
		bool skipAfterConfirm = false)
	{
		_titleText.Text = title;
		_messageText.Text = message;
		_confirmButton.SetAccentColor(isDangerous
			? StyledButton.Theme.DestructiveAccent
			: StyledButton.Theme.Accent);

		if (_buttonContainer.Contains(_cancelButton))
			_buttonContainer.Remove(_cancelButton, false);

		if (_skipButton != null && _buttonContainer.Contains(_skipButton))
			_buttonContainer.Remove(_skipButton, false);

		if (_buttonContainer.Contains(_confirmButton))
			_buttonContainer.Remove(_confirmButton, false);

		if (showSkip && _skipButton == null)
		{
			_skipButton = StyledDialog.CreateSecondaryButton("Skip");
			_skipButton.Clicked += OnSkipClicked;
		}
		else if (!showSkip && _skipButton != null)
		{
			_skipButton.Clicked -= OnSkipClicked;
			_skipButton = null;
		}

		_buttonContainer.Add(_cancelButton);
		if (showSkip && _skipButton != null)
			_buttonContainer.Add(_skipButton);

		_buttonContainer.Add(_confirmButton);

		var estimatedHeight = message.Length > 80 ? 240f : 200f;
		if (showSkip)
			estimatedHeight += 10f;

		_dialogContainer.ResizeHeightTo(estimatedHeight, 0);
		StyledDialog.PlayShowAnimation(this, _dialogContainer);
	}

	/// <summary>
	/// Hides the dialog.
	/// </summary>
	public new void Hide()
	{
		StyledDialog.PlayHideAnimation(this);
		Closed?.Invoke();
	}

	private void OnCancelClicked() => Hide();

	private void OnConfirmClicked()
	{
		Confirmed?.Invoke();
		Hide();
	}

	private void OnSkipClicked()
	{
		Skipped?.Invoke();
		Hide();
	}

	protected override bool OnClick(ClickEvent e) => true;
}
