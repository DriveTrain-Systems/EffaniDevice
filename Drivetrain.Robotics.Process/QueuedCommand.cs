namespace Drivetrain.Robotics.Process
{
	/// <summary>One row of a snapshot of the process engine's command queue (see <see cref="ProcessEngine.GetQueueSnapshot"/>).</summary>
	/// <param name="Text">What to show: the job or command description.</param>
	/// <param name="IsExecuting">True for the command being executed right now.</param>
	/// <param name="IsJob">True for a row that names a job; the job's commands follow it.</param>
	/// <param name="InJob">True for a command that belongs to a named job (shown under it).</param>
	/// <param name="Key">The job or command the row is for (null for a summary row); used to keep a displayed list in step with the queue.</param>
	public record QueuedCommand(string Text, bool IsExecuting, bool IsJob, bool InJob, object? Key)
	{
		/// <summary>The row's text, which is also what accessibility tools read out for it.</summary>
		public override string ToString() => Text;
	}
}
