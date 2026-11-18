using DevExpress.Xpf.Core;
using DevExpress.Xpf.Editors;
using DevExpress.Xpf.LayoutControl;
using Drivetrain.Robotics.Control;
using Drivetrain.Robotics.Process;
using Effani.Device.Control;
using Effani.Device.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Effani.Device.UI
{
	public partial class SettingsControl : UserControl
	{
		public SettingsControl()
		{
			InitializeComponent();
			BuildContents();
			StartRules();
			// a group shown on its own is kept within the width of the page, so that width is worked out again when the page is resized
			SettingsPages.SizeChanged += (s, e) =>
			{
				if (e.WidthChanged && _shown is not null)
					Show(_shown);
			};
		}

		/// <summary>Undo: once the view model has put the settings back (it asks first), the window is closed.</summary>
		private void UndoSettings_Click(object sender, RoutedEventArgs e)
		{
			if (DataContext is UtilitiesViewModel viewModel && viewModel.UndoSettingsChanges())
				Window.GetWindow(this)?.Close();
		}
		private void ComboBoxEdit_SelectedIndexChanged(object sender, System.Windows.RoutedEventArgs e)
		{
			var settingsVersion = ((ComboBoxEdit)e.Source).SelectedItem as SettingsVersion;
			_viewModel.ActivateSettingsVersion(settingsVersion);

		}

		#region Rules
		// What a setting allows is a rule the program checks, not a limit of the field: a field with a limit objects to a value
		// while it is still being typed ("0." on the way to "0.00153"), and refuses to take it. The fields therefore accept
		// anything, and what is wrong with the settings as they stand is listed under the pages, a moment after the last change.

		/// <summary>The least a setting may be; set on its field in the XAML. Checked by <see cref="CheckRules"/>, not by the field.</summary>
		public static readonly DependencyProperty LeastProperty =
			DependencyProperty.RegisterAttached("Least", typeof(double), typeof(SettingsControl), new PropertyMetadata(double.NaN));
		public static double GetLeast(DependencyObject element) => (double)element.GetValue(LeastProperty);
		public static void SetLeast(DependencyObject element, double value) => element.SetValue(LeastProperty, value);
		/// <summary>The most a setting may be; set on its field in the XAML. Checked by <see cref="CheckRules"/>, not by the field.</summary>
		public static readonly DependencyProperty MostProperty =
			DependencyProperty.RegisterAttached("Most", typeof(double), typeof(SettingsControl), new PropertyMetadata(double.NaN));
		public static double GetMost(DependencyObject element) => (double)element.GetValue(MostProperty);
		public static void SetMost(DependencyObject element, double value) => element.SetValue(MostProperty, value);

		/// <summary>
		/// Checks a value against the rule its field carries (<see cref="LeastProperty"/>, <see cref="MostProperty"/>): null when
		/// the value is allowed, otherwise what is wrong with it. Used for the settings pages and by any other window whose
		/// number field has limits (the move windows, the number of samples), so every limit is checked and worded the same way.
		/// </summary>
		public static string? RuleProblem(DependencyObject field, string name, double value) => RuleProblem(name, value, GetLeast(field), GetMost(field));
		private static string? RuleProblem(string name, double value, double least, double most)
		{
			static string Number(double number) => number.ToString("0.#####", System.Globalization.CultureInfo.CurrentCulture);
			if (!double.IsNaN(least) && value < least)
				return $"{name} is {Number(value)}: it should be at least {Number(least)}.";
			if (!double.IsNaN(most) && value > most)
				return $"{name} is {Number(value)}: it should be at most {Number(most)}.";
			return null;
		}
		/// <summary>A setting with a rule: its name on the pages, the setting it is, and what it allows.</summary>
		private sealed record Rule(string Name, string Setting, double Least, double Most);

		/// <summary>Finds the rules on the pages and has them checked whenever a setting changes, or other settings become the active ones.</summary>
		private void StartRules()
		{
			foreach (var page in SettingsPages.Items.OfType<DXTabItem>())
			{
				if (page.Content is not LayoutControlBase root)
					continue;
				foreach (var item in Descendants<LayoutItem>(root))
				{
					if (item.Content is not SpinEdit field || BindingOperations.GetBinding(field, BaseEdit.EditValueProperty)?.Path?.Path is not string setting)
						continue;
					var least = GetLeast(field);
					var most = GetMost(field);
					if (!double.IsNaN(least) || !double.IsNaN(most))
						_rules.Add(new Rule(RuleName(page, item), setting, least, most));
				}
			}
			_rulesTimer.Tick += (s, e) =>
			{
				_rulesTimer.Stop();
				CheckRules();
			};
			SettingsPages.DataContextChanged += (s, e) =>
			{
				if (e.OldValue is System.ComponentModel.INotifyPropertyChanged before)
					before.PropertyChanged -= Settings_Changed;
				if (e.NewValue is System.ComponentModel.INotifyPropertyChanged now)
					now.PropertyChanged += Settings_Changed;
				CheckRules();
			};
			Unloaded += (s, e) => _rulesTimer.Stop();
		}
		/// <summary>A setting changed: the rules are checked once the changes stop, so a value is not reported on while it is being typed.</summary>
		private void Settings_Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			_rulesTimer.Stop();
			_rulesTimer.Start();
		}
		/// <summary>What a field is called in the list of problems: its page, the groups it is in, and its own label (a location's X or Y with the location's name).</summary>
		private static string RuleName(DXTabItem page, LayoutItem item)
		{
			var names = new List<string>();
			for (DependencyObject? element = item; element is not null && !ReferenceEquals(element, page); element = LogicalTreeHelper.GetParent(element))
			{
				if (element is LayoutItem { Label: string label } && !String.IsNullOrWhiteSpace(label))
					names.Insert(0, label.Trim());
				else if (element is LayoutGroup { View: LayoutGroupView.GroupBox, Header: string header } && !String.IsNullOrWhiteSpace(header))
					names.Insert(0, HeadingText(header));
			}
			names.Insert(0, page.Header?.ToString() ?? String.Empty);
			return String.Join(" \u2013 ", names.Where(n => n.Length > 0));
		}
		/// <summary>Checks every setting that has a rule against the settings as they are now, and lists what is wrong under the pages.</summary>
		private void CheckRules()
		{
			var problems = new List<string>();
			if (SettingsPages.DataContext is object settings)
			{
				foreach (var rule in _rules)
				{
					if (settings.GetType().GetProperty(rule.Setting)?.GetValue(settings) is not IConvertible raw)
						continue;
					double value;
					try { value = raw.ToDouble(System.Globalization.CultureInfo.InvariantCulture); }
					catch { continue; }
					if (RuleProblem(rule.Name, value, rule.Least, rule.Most) is string problem)
						problems.Add(problem);
				}
			}
			ProblemsList.ItemsSource = problems;
			ProblemsTitle.Text = problems.Count == 1 ? "1 setting is outside what it allows" : $"{problems.Count} settings are outside what they allow";
			ProblemsPanel.Visibility = problems.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
		}
		private readonly List<Rule> _rules = new();
		private readonly System.Windows.Threading.DispatcherTimer _rulesTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };
		#endregion

		#region The settings of one axis
		/// <summary>One field of an axis's settings: what it is called, its description, and the editor on these pages it is a copy of.</summary>
		private sealed record AxisField(string Label, object? ToolTip, bool IsConfigSetting, BaseEdit Editor);
		/// <summary>The fields found under one heading, and the cautions (by the name of the location they are about) shown with them.</summary>
		private sealed class AxisSection(string title)
		{
			public string Title { get; } = title;
			public List<AxisField> Fields { get; } = new();
			public List<string> Cautions { get; } = new();
		}

		/// <summary>
		/// Builds a panel of the settings that belong to one axis, for showing beside the axis on the Component Control tab. The
		/// fields are copies of the ones on these pages: the same name, description, limits and setting, so a change made in one
		/// shows in the other. They are found by the axis's name: the groups titled with it (its motor settings, its driver and
		/// endstop connections) and the rows labelled with it in the lists that have a row per fluid (volumes, syringe travel,
		/// its port's location). Nothing here names a setting, so a field added to these pages appears in the panel too.
		/// </summary>
		/// <param name="axisLabel">The axis's name as the axis list shows it, e.g. "Table X" or "DNA Reagent".</param>
		/// <param name="viewModel">The view model of the Tools window: the settings, the cautions and Generate Config come from it.</param>
		public FrameworkElement CreateAxisSettingsPanel(string axisLabel, UtilitiesViewModel viewModel)
		{
			var names = axisLabel switch
			{
				"Aspirate" => new[] { "Aspirate", "Aspirate Actuator", "Aspiration" },
				"Basal" => new[] { "Basal", "Basal Media" },
				_ => new[] { axisLabel },
			};
			var sections = new List<AxisSection>();
			foreach (var page in SettingsPages.Items.OfType<DXTabItem>())
			{
				if (page.Content is LayoutControlBase root)
					CollectAxisFields(root, names, page.Header?.ToString() ?? String.Empty, new List<LayoutGroup>(), sections);
			}

			var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 4), MaxWidth = 1080, HorizontalAlignment = HorizontalAlignment.Left };
			// the settings object is replaced when another settings version is activated: the panel follows it
			panel.SetBinding(DataContextProperty, new Binding(nameof(UtilitiesViewModel.Settings)) { Source = viewModel });
			if (TryFindResource(typeof(SpinButton)) is Style spinButtonStyle)
				panel.Resources.Add(typeof(SpinButton), spinButtonStyle); // spin arrows wide enough for a finger, as on these pages
			var configColor = TryFindResource("ConfigSettingColor") as System.Windows.Media.Brush;
			var lockedStyle = TryFindResource("DeviceControlSetting") as Style;
			var anyConfigSetting = false;
			foreach (var section in sections.Where(s => s.Fields.Count > 0))
			{
				panel.Children.Add(new TextBlock { Text = section.Title, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) });
				var fields = new WrapPanel();
				foreach (var field in section.Fields)
				{
					if (CopyEditor(field.Editor, lockedStyle, viewModel.IsAssayRunning) is not FrameworkElement editor)
						continue;
					anyConfigSetting |= field.IsConfigSetting;
					var label = new TextBlock { Text = field.Label, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 2) };
					if (field.IsConfigSetting)
					{
						label.FontWeight = FontWeights.SemiBold;
						if (configColor is not null)
							label.Foreground = configColor;
					}
					fields.Children.Add(new StackPanel { Width = 168, Margin = new Thickness(0, 0, 12, 8), VerticalAlignment = VerticalAlignment.Bottom, ToolTip = field.ToolTip, Children = { label, editor } });
				}
				panel.Children.Add(fields);
				foreach (var location in section.Cautions)
				{
					var caution = new TextBlock { Style = TryFindResource("LocationCaution") as Style, MaxWidth = 700 };
					caution.SetBinding(TextBlock.TextProperty, new Binding($"{nameof(UtilitiesViewModel.LocationCautions)}[{location}]") { Source = viewModel });
					panel.Children.Add(caution);
				}
			}
			if (panel.Children.Count == 0)
				panel.Children.Add(new TextBlock { Text = "This axis has no settings of its own.", Opacity = 0.7 });
			if (anyConfigSetting)
			{
				// the blue ones only reach the board once its config is generated: say so, with the button to do it when it is needed
				var note = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0), FontSize = 14 };
				note.Inlines.Add(new System.Windows.Documents.Run("Blue settings") { FontWeight = FontWeights.SemiBold, Foreground = configColor ?? note.Foreground });
				note.Inlines.Add(new System.Windows.Documents.Run(" are written to the board's config: after changing one, press Generate Config so the board uses it."));
				var generate = new SimpleButton { Style = TryFindResource("GenerateConfigButton") as Style, DataContext = viewModel, CornerRadius = new CornerRadius(5), Padding = new Thickness(16, 0, 16, 0), MinHeight = 48 };
				generate.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty, new Binding("GenerateConfigFilesCommand"));
				panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0), Children = { note, generate } });
			}
			return panel;
		}
		/// <summary>Walks a page for the fields that belong to the axis called any of <paramref name="names"/>, adding them to <paramref name="sections"/>.</summary>
		private void CollectAxisFields(LayoutControlBase layout, string[] names, string pageTitle, List<LayoutGroup> groups, List<AxisSection> sections)
		{
			AxisSection Section(string title)
			{
				var section = sections.FirstOrDefault(s => s.Title == title);
				if (section is null)
					sections.Add(section = new AxisSection(title));
				return section;
			}
			bool IsConfig(LayoutItem item) => item.Style is not null && ReferenceEquals(item.Style, TryFindResource("ConfigSetting"));

			var children = layout.GetLogicalChildren(false).Cast<object>().ToList();
			for (var index = 0; index < children.Count; index++)
			{
				var child = children[index];
				if (child is LayoutGroup { View: LayoutGroupView.GroupBox, Header: string { Length: > 0 } header } group)
				{
					if (names.Contains(HeadingText(header)))
					{
						// a group that is the axis's own: every field in it, under the heading of the group it is one of (or its own)
						var section = Section(groups.Count > 0 ? HeadingText((string)groups[^1].Header) : HeadingText(header));
						foreach (var item in Descendants<LayoutItem>(group))
						{
							if (item.Content is BaseEdit editor && item.Label is string label)
								section.Fields.Add(new AxisField(label, item.ToolTip, IsConfig(item), editor));
						}
						continue;
					}
					groups.Add(group);
					CollectAxisFields(group, names, pageTitle, groups, sections);
					groups.RemoveAt(groups.Count - 1);
				}
				else if (child is LayoutItem { Label: string itemLabel } row && names.Contains(itemLabel) && groups.Count > 0
					&& !((string)groups[^1].Header).Contains("Sensor", StringComparison.OrdinalIgnoreCase)) // the DNA and NADH sensors are not the pumps of those names
				{
					// a row for this fluid in a list with a row per fluid: named after the list
					var listTitle = (string)groups[^1].Header;
					if (!listTitle.Contains('(') && groups.Count > 1 && (string)groups[^2].Header is string outer && outer.Contains('('))
						listTitle += " " + outer[outer.IndexOf('(')..]; // the unit is on the group the list is in
					var section = Section(pageTitle);
					if (row.Content is BaseEdit editor)
						section.Fields.Add(new AxisField(listTitle, row.ToolTip, IsConfig(row), editor));
					else
					{
						// a location: its X and its Y, and the caution that follows it on the page
						foreach (var part in row.Content is LayoutControlBase parts ? Descendants<LayoutItem>(parts) : Enumerable.Empty<LayoutItem>())
						{
							if (part.Content is BaseEdit partEditor && part.Label is string partLabel)
								section.Fields.Add(new AxisField($"Location {partLabel}", row.ToolTip, IsConfig(row) || IsConfig(part), partEditor));
						}
						if (index + 1 < children.Count && children[index + 1] is TextBlock next
							&& BindingOperations.GetBinding(next, TextBlock.TextProperty)?.Path?.Path is string path
							&& path.IndexOf("LocationCautions[", StringComparison.Ordinal) is var at and >= 0)
						{
							var start = at + "LocationCautions[".Length;
							section.Cautions.Add(path[start..path.IndexOf(']', start)]);
						}
					}
				}
				else if (child is LayoutControlBase arrangement)
					CollectAxisFields(arrangement, names, pageTitle, groups, sections); // a group without a heading only arranges what is in it
			}
		}
		private static IEnumerable<T> Descendants<T>(LayoutControlBase layout) where T : class
		{
			foreach (var child in layout.GetLogicalChildren(false))
			{
				if (child is T match)
					yield return match;
				if (child is LayoutControlBase inner)
				{
					foreach (var deeper in Descendants<T>(inner))
						yield return deeper;
				}
				else if (child is LayoutItem { Content: LayoutControlBase content })
				{
					foreach (var deeper in Descendants<T>(content))
						yield return deeper;
				}
			}
		}
		/// <summary>
		/// A new editor for the setting an editor on these pages edits, with the same limits and look. A setting that is locked on
		/// these pages while an assay runs is locked in the copy too. Null for a kind of editor that is not copied.
		/// </summary>
		private FrameworkElement? CopyEditor(BaseEdit source, Style? lockedStyle, bool isAssayRunning)
		{
			if (BindingOperations.GetBinding(source, BaseEdit.EditValueProperty)?.Path?.Path is not string setting)
				return null;
			BaseEdit copy;
			if (source is SpinEdit spin)
				copy = new SpinEdit { IsFloatValue = spin.IsFloatValue, Increment = spin.Increment, EditValueType = spin.EditValueType, Mask = spin.Mask };
			else if (source is ComboBoxEdit combo)
				copy = new ComboBoxEdit { ItemsSource = combo.ItemsSource, ValueMember = combo.ValueMember, DisplayMember = combo.DisplayMember, IsTextEditable = combo.IsTextEditable, MinWidth = 160 };
			else
				return null;
			copy.Style = source.Style ?? TryFindResource(source.GetType()) as Style;
			copy.HorizontalAlignment = HorizontalAlignment.Left;
			copy.SetBinding(BaseEdit.EditValueProperty, new Binding(setting) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
			if (isAssayRunning && lockedStyle is not null && ReferenceEquals(source.Style, lockedStyle))
			{
				copy.IsEnabled = false;
				ToolTipService.SetShowOnDisabled(copy, true);
				copy.ToolTip = "This setting is in use by the device control while an assay is running, so it cannot be changed until the assay is finished or canceled.";
			}
			return copy;
		}
		#endregion

		#region Notes
		/// <summary>
		/// A sentence or two saying what the settings of a page (tab item) or group control; set in the XAML and shown under the page
		/// while that page or group is on show.
		/// </summary>
		public static readonly DependencyProperty NoteProperty =
			DependencyProperty.RegisterAttached("Note", typeof(string), typeof(SettingsControl), new PropertyMetadata(null));
		public static string? GetNote(DependencyObject element) => (string?)element.GetValue(NoteProperty);
		public static void SetNote(DependencyObject element, string? value) => element.SetValue(NoteProperty, value);
		#endregion

		#region Table of contents
		/// <summary>What a heading of the table of contents shows: a whole page of settings, or one group on it.</summary>
		private sealed record ContentsEntry(DXTabItem Page, FrameworkElement? Group);

		/// <summary>
		/// Fills the table of contents from the settings pages: a major heading for each page (tab item) and, nested under it the way
		/// they are nested on the page, a heading for each group box.
		/// </summary>
		private void BuildContents()
		{
			foreach (var page in SettingsPages.Items.OfType<DXTabItem>())
			{
				var pageHeading = NewHeading(page.Header?.ToString() ?? String.Empty, new ContentsEntry(page, null), 0);
				if (page.Content is LayoutControlBase root)
					AddGroupHeadings(pageHeading, page, root, 1);
				// a page that is a single group needs no sub heading
				if (pageHeading.Items.Count == 1 && ((TreeViewItem)pageHeading.Items[0]).Items.Count == 0)
					pageHeading.Items.Clear();
				Contents.Items.Add(pageHeading);
			}
			if (Contents.Items.Count > 0)
				((TreeViewItem)Contents.Items[0]).IsSelected = true;
		}
		private static void AddGroupHeadings(TreeViewItem parent, DXTabItem page, LayoutControlBase layout, int level)
		{
			// the logical children: what was put in the layout, not the frame and scroll bars it adds for itself
			foreach (var child in layout.GetLogicalChildren(false))
			{
				if (child is LayoutGroup { View: LayoutGroupView.GroupBox, Header: string { Length: > 0 } header } group)
				{
					var heading = NewHeading(header, new ContentsEntry(page, group), level);
					parent.Items.Add(heading);
					AddGroupHeadings(heading, page, group, level + 1);
				}
				else if (child is LayoutControlBase arrangement)
					AddGroupHeadings(parent, page, arrangement, level); // a group without a heading, or a flow layout, only arranges what is in it
			}
		}
		/// <summary>A heading sized for a touchscreen; the text wraps rather than running out of the list.</summary>
		private static TreeViewItem NewHeading(string text, ContentsEntry entry, int level)
		{
			return new TreeViewItem
			{
				Tag = entry,
				Header = new TextBlock
				{
					Text = HeadingText(text),
					TextWrapping = TextWrapping.Wrap,
					FontSize = level == 0 ? 20 : 17,
					FontWeight = level == 0 ? FontWeights.SemiBold : FontWeights.Normal,
					Padding = new Thickness(6, 12, 6, 12),
					Width = Math.Max(140, 325 - (level * 19)), // the same right edge at every level of indent
				},
			};
		}
		/// <summary>A name without its notes in parentheses (units and the like); those stay on the group itself.</summary>
		private static string HeadingText(string name)
		{
			var text = new System.Text.StringBuilder();
			var depth = 0;
			foreach (var character in name)
			{
				if (character == '(')
					depth++;
				else if (character == ')')
					depth = Math.Max(0, depth - 1);
				else if (depth == 0)
					text.Append(character);
			}
			var heading = String.Join(' ', text.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
			return heading.Length > 0 ? heading : name;
		}
		private void Contents_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
		{
			if (e.NewValue is not TreeViewItem { Tag: ContentsEntry entry } heading)
				return;

			// touching a heading opens it, and only one major heading is open at a time: no small arrows to hit
			heading.IsExpanded = true;
			var major = heading;
			while (major.Parent is TreeViewItem parent)
			{
				major = parent;
				major.IsExpanded = true;
			}
			foreach (var other in Contents.Items.OfType<TreeViewItem>())
			{
				if (!ReferenceEquals(other, major))
					other.IsExpanded = false;
			}

			// a page with several groups is too much for a small screen at once: its major heading opens to its first group
			if (entry.Group is null && heading.Items.Count > 0)
			{
				Dispatcher.BeginInvoke(() =>
				{
					if (heading.IsSelected) // unless another heading was chosen in the meantime
						((TreeViewItem)heading.Items[0]).IsSelected = true;
				});
				return;
			}

			Show(entry);
		}
		/// <summary>Shows a page of settings, and when the heading is for a group on it, only that group.</summary>
		private void Show(ContentsEntry entry)
		{
			RestoreChanged();
			SettingsPages.SelectedItem = entry.Page;
			_shown = entry;
			ShowNote(entry);

			// only the chosen group stays: at every level from it up to the page, whatever is beside it is hidden. What it is nested
			// in stays as a frame around it, sized to it rather than stretched across the page.
			var nesting = new List<FrameworkElement>(); // the group, then what it is nested in, outwards
			for (var current = entry.Group; current is not null && !ReferenceEquals(current, entry.Page.Content); current = current.Parent as FrameworkElement)
			{
				if (current.Parent is not LayoutControlBase parent)
					break;
				foreach (var sibling in parent.GetLogicalChildren(false)) // not its own frame and scroll bars
				{
					if (!ReferenceEquals(sibling, current))
						Change(sibling, VisibilityProperty, Visibility.Collapsed);
				}
				Change(current, HorizontalAlignmentProperty, HorizontalAlignment.Left);
				nesting.Add(current);
			}

			// no wider than the page, so the groups inside wrap onto more rows rather than running off its right edge; each level of
			// nesting has a little less room, for the frame around it
			var room = SettingsPages.ActualWidth - 50;
			for (var level = 0; room > 0 && level < nesting.Count; level++)
				Change(nesting[nesting.Count - 1 - level], MaxWidthProperty, Math.Max(240, room - (level * 30)));
		}
		/// <summary>Shows what the settings on show control: the note of the chosen group, else of what it is nested in, else of its page.</summary>
		private void ShowNote(ContentsEntry entry)
		{
			string? note = null;
			for (var current = entry.Group; note is null && current is not null && !ReferenceEquals(current, entry.Page.Content); current = current.Parent as FrameworkElement)
				note = GetNote(current);
			note ??= GetNote(entry.Page);
			NoteText.Text = note ?? String.Empty;
			NotePanel.Visibility = String.IsNullOrWhiteSpace(note) ? Visibility.Collapsed : Visibility.Visible;
		}
		private void Change(FrameworkElement element, DependencyProperty property, object value)
		{
			_changed.Add((element, property, BindingOperations.GetBindingBase(element, property), element.ReadLocalValue(property)));
			element.SetValue(property, value);
		}
		/// <summary>Puts back what <see cref="Show"/> changed for the previous heading, including a binding or a value that was set in the XAML.</summary>
		private void RestoreChanged()
		{
			for (var index = _changed.Count - 1; index >= 0; index--)
			{
				var (element, property, binding, local) = _changed[index];
				if (binding is not null)
					BindingOperations.SetBinding(element, property, binding);
				else if (local == DependencyProperty.UnsetValue)
					element.ClearValue(property);
				else
					element.SetValue(property, local);
			}
			_changed.Clear();
		}

		private readonly List<(FrameworkElement Element, DependencyProperty Property, BindingBase? Binding, object Local)> _changed = new();
		private ContentsEntry? _shown;
		#endregion

		UtilitiesViewModel _viewModel => DataContext as UtilitiesViewModel;
    }
}
