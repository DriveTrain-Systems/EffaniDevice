using DevExpress.Xpf.Editors;
using DevExpress.Xpf.LayoutControl;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Effani.Device.UI
{
	/// <summary>
	/// Typing on the touchscreen. When a field that takes typed input is clicked or touched, the on-screen keyboard is shown and a
	/// copy of the field opens just above it (<see cref="OnScreenInputView"/>), so what is typed can be seen while the keyboard
	/// covers the screen. A field reached another way (the Tab key, or focus given by the program) is left alone. The whole
	/// behaviour can be turned off, with the button in the lower right corner of every window (<see cref="OnScreenInputToggle"/>).
	/// </summary>
	public static class OnScreenInput
	{
		/// <summary>Starts watching for clicks on fields, in every window; call once when the application starts.</summary>
		public static void Register()
		{
			if (_registered)
				return;
			_registered = true;
			// touches reach here too: a touch on a field is turned into these mouse events
			EventManager.RegisterClassHandler(typeof(BaseEdit), UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(Editor_MouseDown), true);
			EventManager.RegisterClassHandler(typeof(BaseEdit), UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(Editor_MouseUp), true);
		}
		/// <summary>Whether a click on a field opens the keyboard and the copy of the field. On until it is turned off.</summary>
		public static bool Enabled
		{
			get => _enabled;
			set
			{
				if (_enabled == value)
					return;
				_enabled = value;
				EnabledChanged?.Invoke(null, EventArgs.Empty);
			}
		}
		public static event EventHandler? EnabledChanged;
		/// <summary>False keeps the Windows keyboard out of it, so the copy of the field can be tried on a machine without one.</summary>
		internal static bool UseTouchKeyboard { get; set; } = true;

		/// <summary>
		/// Whether a field is one that is typed in: a text, number or password editor that can be changed. Lists that are picked
		/// from, editors in a grid's cells and anything read-only are not.
		/// </summary>
		internal static bool IsTypingField(BaseEdit editor)
		{
			return editor is TextEditBase and not PopupBaseEdit
				&& editor.IsEnabled && !editor.IsReadOnly
				&& editor.EditMode == EditMode.Standalone
				&& Window.GetWindow(editor) is not OnScreenInputView;
		}
		/// <summary>
		/// What a field is called, from what is around it: the labels of the layout items it is in and the title of their group, or
		/// the text just before it, or its own prompt.
		/// </summary>
		internal static string? LabelFor(BaseEdit editor)
		{
			var names = new List<string>();
			for (var element = Parent(editor); element is not null; element = Parent(element))
			{
				if (element is LayoutItem { Label: string label } && !String.IsNullOrWhiteSpace(label))
					names.Insert(0, label.Trim());
				else if (element is LayoutGroup { View: LayoutGroupView.GroupBox, Header: string header } && !String.IsNullOrWhiteSpace(header))
				{
					names.Insert(0, header.Trim());
					break;
				}
			}
			if (names.Count > 0)
				return String.Join(" \u2013 ", names);

			if (VisualTreeHelper.GetParent(editor) is Panel panel)
			{
				for (var index = panel.Children.IndexOf(editor) - 1; index >= 0; index--)
				{
					if (panel.Children[index] is TextBlock { Text: { Length: > 0 } text })
						return text.Trim().TrimEnd(':');
					if (panel.Children[index] is not TextBlock)
						break;
				}
			}
			return (editor as TextEdit)?.NullText is { Length: > 0 } prompt ? prompt : null;
		}
		/// <summary>The description of a field: its own tooltip, or that of the layout item it is in.</summary>
		internal static string? HintFor(BaseEdit editor)
		{
			for (DependencyObject? element = editor; element is not null; element = Parent(element))
			{
				if (element is FrameworkElement { ToolTip: string tip } && !String.IsNullOrWhiteSpace(tip))
					return tip;
				if (element is LayoutGroup)
					break;
			}
			return null;
		}
		/// <summary>Shows the keyboard and the copy of a field, and puts what was typed into the field unless it is cancelled.</summary>
		internal static void Open(BaseEdit editor)
		{
			if (_open)
				return;
			_open = true;
			try
			{
				var owner = Window.GetWindow(editor)
					?? Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
					?? Application.Current?.MainWindow;
				var view = new OnScreenInputView(editor) { Owner = owner };
				view.ShowDialog();
				if (view.Accepted)
					view.ApplyTo(editor);
			}
			finally
			{
				_open = false;
			}
		}

		private static void Editor_MouseDown(object sender, MouseButtonEventArgs e)
		{
			_pressed = Enabled && sender is BaseEdit editor && IsTypingField(editor) && !IsOnButton(e.OriginalSource as DependencyObject, editor) ? editor : null;
		}
		private static void Editor_MouseUp(object sender, MouseButtonEventArgs e)
		{
			if (_pressed is null || !ReferenceEquals(_pressed, sender))
				return;
			var editor = _pressed;
			_pressed = null;
			// once this click is over, so the field has finished with the mouse
			editor.Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Open(editor));
		}
		/// <summary>Whether a click landed on one of a field's own buttons (the arrows of a number box, say) rather than its text.</summary>
		private static bool IsOnButton(DependencyObject? source, BaseEdit editor)
		{
			for (var element = source; element is not null && !ReferenceEquals(element, editor); element = Parent(element))
			{
				if (element is ButtonBase || element is ButtonsControl)
					return true;
			}
			return false;
		}
		private static DependencyObject? Parent(DependencyObject element)
		{
			var parent = element is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : null;
			return parent ?? LogicalTreeHelper.GetParent(element);
		}

		private static bool _registered;
		private static bool _enabled = true;
		private static bool _open;
		private static BaseEdit? _pressed;
	}

	/// <summary>
	/// The button in the lower right corner of a window that turns typing with the on-screen keyboard (<see cref="OnScreenInput"/>)
	/// on and off. Every window's button shows the same state.
	/// </summary>
	public class OnScreenInputToggle : ToggleButton
	{
		public OnScreenInputToggle()
		{
			Width = 60;
			Height = 60;
			Padding = new Thickness(0);
			Focusable = false;
			HorizontalAlignment = HorizontalAlignment.Right;
			VerticalAlignment = VerticalAlignment.Bottom;
			Margin = new Thickness(0, 0, 6, 6);
			Cursor = Cursors.Hand;
			_state = new TextBlock { FontSize = 12, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };
			Content = new StackPanel
			{
				Children =
				{
					new TextBlock { Text = "\u2328", FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = 26, HorizontalAlignment = HorizontalAlignment.Center },
					_state,
				},
			};
			Loaded += (s, e) =>
			{
				OnScreenInput.EnabledChanged -= OnScreenInput_EnabledChanged;
				OnScreenInput.EnabledChanged += OnScreenInput_EnabledChanged;
				Refresh();
			};
			Unloaded += (s, e) => OnScreenInput.EnabledChanged -= OnScreenInput_EnabledChanged;
			Checked += (s, e) => OnScreenInput.Enabled = true;
			Unchecked += (s, e) => OnScreenInput.Enabled = false;
			Refresh();
		}
		private void OnScreenInput_EnabledChanged(object? sender, EventArgs e) => Refresh();
		private void Refresh()
		{
			IsChecked = OnScreenInput.Enabled;
			_state.Text = OnScreenInput.Enabled ? "ON" : "OFF";
			ToolTip = OnScreenInput.Enabled
				? "On-screen keyboard is on: touching a field opens the keyboard and a copy of the field above it. Touch to turn it off."
				: "On-screen keyboard is off: fields are typed in directly. Touch to turn it on.";
		}
		private readonly TextBlock _state;
	}
}
