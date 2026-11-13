using Drivetrain.Robotics.Process;
using System.Collections.ObjectModel;

namespace Effani.Device.UI
{
	/// <summary>
	/// The process engine's command queue for display, with the executing command flagged. Call <see cref="Refresh"/> on the UI thread
	/// (from a timer); the list is updated in place, so a list box showing it keeps its scroll position.
	/// </summary>
	public sealed class CommandQueueDisplay
	{
		public ObservableCollection<QueuedCommand> Items { get; } = new();

		public void Refresh(ProcessEngine processEngine)
		{
			var rows = processEngine.GetQueueSnapshot();

			// rows that have finished drop off the front: remove them rather than rewriting every row below
			if (rows.Count > 0 && rows[0].Key is not null)
			{
				var first = -1;
				for (var i = 0; i < Items.Count; i++)
				{
					if (ReferenceEquals(Items[i].Key, rows[0].Key))
					{
						first = i;
						break;
					}
				}
				for (var i = 0; i < first; i++)
					Items.RemoveAt(0);
			}

			for (var i = 0; i < rows.Count; i++)
			{
				if (i >= Items.Count)
					Items.Add(rows[i]);
				else if (!Items[i].Equals(rows[i]))
					Items[i] = rows[i];
			}
			while (Items.Count > rows.Count)
				Items.RemoveAt(Items.Count - 1);
		}
	}
}
