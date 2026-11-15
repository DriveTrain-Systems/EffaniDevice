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
			// a group shown on its own is kept within the width of the page, so that width is worked out again when the page is resized
			SettingsPages.SizeChanged += (s, e) =>
			{
				if (e.WidthChanged && _shown is not null)
					Show(_shown);
			};
		}

		private void ComboBoxEdit_SelectedIndexChanged(object sender, System.Windows.RoutedEventArgs e)
		{
			var settingsVersion = ((ComboBoxEdit)e.Source).SelectedItem as SettingsVersion;
			_viewModel.ActivateSettingsVersion(settingsVersion);

		}

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
