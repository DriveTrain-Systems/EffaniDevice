using System;
using System.Linq;

namespace Drivetrain.Robotics.Process
{
	public class Job
	{
        public Job(String description, Command[]? commands = null)
        {
			Description = description;
			if (commands is not null && commands.Any())
                Commands.AddRange(commands);
        }

        public List<Command> Commands { get; set; } = new List<Command>();
		public string Description { get; set; }
		/// <summary>
		/// True once the process engine is done with the job: it ran to its end, failed, was stopped part way, or was discarded
		/// from the queue without running. What issued the job can watch this to know when it is over.
		/// </summary>
		public bool IsFinished => _finished;
		internal void Finish() => _finished = true;
		private volatile bool _finished;
	}
}
