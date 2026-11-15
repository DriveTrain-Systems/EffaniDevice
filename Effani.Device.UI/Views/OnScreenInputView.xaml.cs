using DevExpress.Xpf.Core;
using DevExpress.Xpf.Editors;
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Effani.Device.UI
{
	/// <summary>
	/// A copy of a field, shown just above the on-screen keyboard so that what is typed can be seen while the keyboard covers the
	/// screen. OK (or Enter) puts what was typed into the field; Cancel (or Esc) leaves the field as it was. See
	/// <see cref="OnScreenInput"/>.
	/// </summary>
	public partial class OnScreenInputView : ThemedWindow
	{
		/// <param name="field">The field that was touched: the copy is the same kind of editor, with the same limits and value.</param>
		public OnScreenInputView(BaseEdit field)
		{
			InitializeComponent();
			ControlBoxButtonSet = ControlBoxButtons.None;
			_editor = CopyOf(field);
			_editor.MinHeight = 64;
			_editor.FontSize = 26;
			EditorHost.Content = _editor;

			Caption.Text = OnScreenInput.LabelFor(field) ?? "Type a value";
			var hint = OnScreenInput.HintFor(field);
			if (field is SpinEdit { MinValue: decimal minimum, MaxValue: decimal maximum })
				hint = (hint is null ? String.Empty : hint.TrimEnd().TrimEnd('.') + ". ") + $"A valid value is from {minimum:0.###} to {maximum:0.###}.";
			Hint.Text = hint ?? String.Empty;
			Hint.Visibility = String.IsNullOrEmpty(hint) ? Visibility.Collapsed : Visibility.Visible;

			Loaded += (s, e) =>
			{
				Place();
				_editor.Focus();
				_editor.SelectAll();
				if (OnScreenInput.UseTouchKeyboard)
					TouchKeyboard.Show();
				// the keyboard takes a moment to come up, and can be moved or resized while this is open
				_follow.Start();
			};
			SizeChanged += (s, e) => Place();
			_follow.Tick += (s, e) => Place();
			Closed += (s, e) =>
			{
				_follow.Stop();
				if (OnScreenInput.UseTouchKeyboard)
					TouchKeyboard.Hide();
			};
		}

		/// <summary>True once the window is closed with OK.</summary>
		public bool Accepted { get; private set; }

		/// <summary>Puts what was typed into the field that was touched, and lets the field check it the way it does after typing.</summary>
		public void ApplyTo(BaseEdit field)
		{
			if (field is PasswordBoxEdit password && _editor is PasswordBoxEdit typed)
				password.Password = typed.Password;
			else
				field.EditValue = _editor.EditValue;
			field.DoValidate();
		}

		private void Accept_Click(object sender, RoutedEventArgs e)
		{
			_editor.DoValidate(); // takes in what was typed last
			if (_editor.HasValidationError)
			{
				Problem.Text = "That value cannot be used. Correct it, or press Cancel.";
				Problem.Visibility = Visibility.Visible;
				return;
			}
			Accepted = true;
			Close();
		}
		private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

		/// <summary>An editor of the same kind as a field, with the same limits and the same value.</summary>
		private static BaseEdit CopyOf(BaseEdit field)
		{
			switch (field)
			{
				case SpinEdit spin:
					var number = new SpinEdit
					{
						MinValue = spin.MinValue,
						MaxValue = spin.MaxValue,
						IsFloatValue = spin.IsFloatValue,
						Increment = spin.Increment,
						EditValueType = spin.EditValueType,
						AllowNullInput = spin.AllowNullInput,
						HorizontalContentAlignment = HorizontalAlignment.Left,
					};
					if (!String.IsNullOrEmpty(spin.Mask))
					{
						number.MaskType = spin.MaskType;
						number.Mask = spin.Mask;
					}
					number.EditValue = spin.EditValue;
					return number;
				case PasswordBoxEdit password:
					return new PasswordBoxEdit
					{
						ShowPasswordButtonVisibility = ShowPasswordButtonVisibility.Auto,
						NullText = password.NullText,
						Password = password.Password,
					};
				default:
					var text = new TextEdit { EditValue = field.EditValue };
					if (field is TextEdit original)
					{
						text.NullText = original.NullText;
						text.MaxLength = original.MaxLength;
						if (!String.IsNullOrEmpty(original.Mask))
						{
							text.MaskType = original.MaskType;
							text.Mask = original.Mask;
						}
					}
					return text;
			}
		}
		/// <summary>
		/// Puts the window just above the on-screen keyboard, centred; while the keyboard's place is not known, in the upper part of
		/// the screen, which the keyboard does not reach.
		/// </summary>
		private void Place()
		{
			if (ActualHeight <= 0)
				return;
			var screenWidth = SystemParameters.PrimaryScreenWidth;
			var screenHeight = SystemParameters.PrimaryScreenHeight;
			var top = screenHeight * 0.14;
			if (OnScreenInput.UseTouchKeyboard && TouchKeyboard.Bounds is Rect keyboard)
			{
				// the keyboard's place is in screen pixels; the window's is in WPF units
				var toUnits = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
				var keyboardTop = toUnits.Transform(new Point(keyboard.Left, keyboard.Top)).Y;
				if (keyboardTop > ActualHeight)
					top = keyboardTop - ActualHeight - 10;
			}
			Left = Math.Max(0, (screenWidth - ActualWidth) / 2);
			Top = Math.Max(0, Math.Min(top, screenHeight - ActualHeight));
		}

		private readonly BaseEdit _editor;
		private readonly DispatcherTimer _follow = new() { Interval = TimeSpan.FromMilliseconds(300) };
	}
}
