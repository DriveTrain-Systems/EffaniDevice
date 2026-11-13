using System;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace Effani.Device.UI
{
	/// <summary>
	/// A list box for lists whose items are replaced or removed all the time (the command queue, console messages). WPF's
	/// accessibility support keeps only a weak reference to each item it has described; once a replaced item has been garbage
	/// collected, a screen reader or other accessibility client (or the debugger) asking for that item's name gets a
	/// NullReferenceException from deep inside WPF (ItemAutomationPeer.GetNameCore). This list box answers with an empty name
	/// instead.
	/// </summary>
	public class LiveListBox : ListBox
	{
		protected override AutomationPeer OnCreateAutomationPeer() => new LiveListBoxAutomationPeer(this);

		private sealed class LiveListBoxAutomationPeer(ListBox owner) : ListBoxAutomationPeer(owner)
		{
			protected override ItemAutomationPeer CreateItemAutomationPeer(object item) => new LiveItemAutomationPeer(item, this);
		}

		private sealed class LiveItemAutomationPeer(object item, SelectorAutomationPeer owner) : ListBoxItemAutomationPeer(item, owner)
		{
			protected override string GetNameCore()
			{
				try
				{
					return base.GetNameCore();
				}
				catch (NullReferenceException)
				{
					return String.Empty; // the item has gone: it was replaced or removed and then collected
				}
			}
			protected override string GetItemStatusCore()
			{
				try
				{
					return base.GetItemStatusCore();
				}
				catch (NullReferenceException)
				{
					return String.Empty;
				}
			}
		}
	}
}
