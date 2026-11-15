using Drivetrain.Robotics.Control;
using Serilog;
using System.Collections.Concurrent;

namespace Drivetrain.Robotics.Process
{
	public class ProcessEngine : IDisposable
	{
		#region Constructors
		public ProcessEngine()
		{
			_processThread = new Thread(ProcessJobs);
			_processThread.Start();
			_publishThread = new Thread(PublishMessages);
			_publishThread.Start();
		}
		#endregion

		#region Public methods
		public void Dispose()
		{
			TerminateProcess();
			_publishThread = null;
			_processThread = null;
		}
		public void QueueCommand(Command command)
		{
			QueueJob(new Job(null, [command]));
		}
		public void QueuePriorityCommand(Command command)
		{
			QueuePriorityJob(new Job(null, [command]));
		}
		public void QueueJob(Job job)
		{
			Interlocked.Increment(ref _unfinishedJobs);
			JobQueue.Enqueue(job);
		}
		public void QueuePriorityJob(Job job)
		{
			Interlocked.Increment(ref _unfinishedJobs);
			PriorityJobQueue.Enqueue(job);
		}
		/// <summary>Discards every queued job and stops the job that is currently running after the command it is executing.</summary>
		public void ClearPendingJobs()
		{
			HoldQueueForHighPriorityJob = false;
			while (JobQueue.TryDequeue(out _))
				Interlocked.Decrement(ref _unfinishedJobs);
			while (PriorityJobQueue.TryDequeue(out _))
				Interlocked.Decrement(ref _unfinishedJobs);
			if (_executingJob)
				_abortCurrentJob = true;
		}
		public void TerminateProcess()
		{
			_stopEngine = true;
		}
		#endregion

		#region Private methods
		/// <summary>Runs a job, containing any exception so that one failing command does not stop the engine from running later ones.</summary>
		private void TryDoJob(Job job)
		{
			try
			{
				DoJob(job);
			}
			catch (Exception ex)
			{
				Log.Error(ex, "Exception executing command");
				EnqueueMessage($"Error running command: {ex.Message}");
			}
		}
		private void DoJob(Job job)
		{
			try
			{
				RunJob(job);
			}
			finally
			{
				// only now is the job no longer pending: this covers the moment between taking it off its queue and starting it
				Interlocked.Decrement(ref _unfinishedJobs);
			}
		}
		private void RunJob(Job job)
		{
			if (job.Description is not null)
				EnqueueMessage($"Begin {job.Description}");

			_executingJob = true;
			_currentCommandIndex = 0; // before the job is published, so a snapshot never applies the last job's position to it
			_currentJob = job;
			try
			{
				for (var index = 0; index < job.Commands.Count; index++)
				{
					if (_abortCurrentJob)
						break;

					var command = job.Commands[index];
					_currentCommandIndex = index;

					var descriptionPrefix = job.Description is null ? String.Empty : "     ";
					EnqueueMessage($"{descriptionPrefix}{command.Description}");
					BeginExecutingCommand?.Invoke(this, new DataArgs<Command>(command));
					_executingCommand = command;
					try
					{
						command.Execute();
					}
					finally
					{
						_executingCommand = null;
					}
					EndExecutingCommand?.Invoke(this, new DataArgs<Command>(command));
					if (command is ReadSensor)
						EnqueueMessage($"{descriptionPrefix}Result of {command.Description}: {command.Result}");
					else if (command is SendCommand { ReportResponse: true })
						EnqueueResponse(descriptionPrefix, command);
				}
			}
			finally
			{
				_currentJob = null;
				_executingJob = false;
				_abortCurrentJob = false;
			}

			if (job.Description is not null)
				EnqueueMessage($"End {job.Description}");
		}
		/// <summary>Reports each line of the board's response to a command, or says that there was none.</summary>
		private void EnqueueResponse(string descriptionPrefix, Command command)
		{
			var lines = (command.Result ?? String.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
			if (lines.Length == 0)
			{
				EnqueueMessage($"{descriptionPrefix}No response from the board to {command.Description}");
				return;
			}
			foreach (var line in lines)
				EnqueueMessage($"{descriptionPrefix}Response: {line}");
		}
		private void EnqueueMessage(string message)
		{
			Messages.Enqueue(message);
		}
		private void ProcessJobs()
		{
			try
			{
				while (!_stopEngine)
				{
					while (PriorityJobQueue.TryDequeue(out var job))
					{
						TryDoJob(job);
					}
					while (!HoldQueueForHighPriorityJob && JobQueue.TryDequeue(out var job))
					{
						TryDoJob(job);
						if (PriorityJobQueue.Any())
							break;
					}
					Thread.Sleep(100);
				}
			}
			catch (Exception ex)
			{
				Log.Error(ex, "Exception executing command");
				EnqueueMessage(ex.Message);
			}
		}
		private void PublishMessages()
		{
			while (!_stopEngine)
			{
				while (Messages.TryDequeue(out var message))
				{
					MessageReceived?.Invoke(this, new DataArgs<string>(message));
				}
				Thread.Sleep(500);
			}
		}
		#endregion

		#region Properties
		/// <summary>
		/// True while any job is queued (including jobs held back by <see cref="HoldQueueForHighPriorityJob"/>) or executing.
		/// Anything that rebuilds the device configuration should wait until this is false.
		/// </summary>
		/// <summary>
		/// A snapshot of everything still to run, in the order it will run: the rest of the job being executed (the executing command
		/// flagged, with its countdown to giving up on the board), then the priority jobs, then the other queued jobs. Named jobs get a
		/// row of their own with their commands under it. At most <paramref name="maxRows"/> rows are returned; a last row says how many
		/// more commands there are.
		/// </summary>
		public IReadOnlyList<QueuedCommand> GetQueueSnapshot(int maxRows = 500)
		{
			var rows = new List<QueuedCommand>();
			var hidden = 0;
			void AddJob(Job job, IEnumerable<Command> commands, Command? executing)
			{
				var named = job.Description is not null;
				if (named)
				{
					if (rows.Count < maxRows)
						rows.Add(new QueuedCommand(job.Description!, false, true, false, job));
				}
				foreach (var command in commands)
				{
					if (rows.Count >= maxRows)
					{
						hidden++;
						continue;
					}
					var isExecuting = ReferenceEquals(command, executing);
					var text = Describe(command);
					rows.Add(new QueuedCommand(isExecuting ? $"▶ {text}" : text, isExecuting, false, named, command, isExecuting ? Countdown(command) : null));
				}
			}

			var current = _currentJob;
			if (current is not null)
				AddJob(current, CommandsOf(current).Skip(_currentCommandIndex), _executingCommand);
			foreach (var job in PriorityJobQueue)
				AddJob(job, CommandsOf(job), null);
			foreach (var job in JobQueue)
				AddJob(job, CommandsOf(job), null);
			if (hidden > 0)
				rows.Add(new QueuedCommand($"… and {hidden} more commands", false, false, false, null));
			return rows;
		}
		private static Command[] CommandsOf(Job job)
		{
			try
			{
				return job.Commands.ToArray();
			}
			catch (Exception)
			{
				return []; // the job's list was being changed while it was copied
			}
		}
		private static string? Countdown(Command command)
		{
			try
			{
				return command.TimeoutCountdown;
			}
			catch (Exception)
			{
				return null;
			}
		}
		private static string Describe(Command command)
		{
			try
			{
				return command.Description;
			}
			catch (Exception)
			{
				return command.GetType().Name; // a description that depends on state that is not ready yet
			}
		}
		/// <summary>The command being executed right now, or null.</summary>
		public Command? ExecutingCommand => _executingCommand;
		public bool IsBusy => Volatile.Read(ref _unfinishedJobs) > 0;
		public ConcurrentQueue<string> Data { get; } = new();
		public bool HoldQueueForHighPriorityJob { get; set; }
		public ConcurrentQueue<Job> JobQueue { get; } = new();
		public ConcurrentQueue<string> Messages { get; } = new();
		public ConcurrentQueue<Job> PriorityJobQueue { get; } = new();
		#endregion

		#region Events
		public event EventHandler<DataArgs<KeyValuePair<string, string>>>? DataReceived;
		public event EventHandler<DataArgs<string>>? MessageReceived;
		public event EventHandler<DataArgs<Command>>? BeginExecutingCommand;
		public event EventHandler<DataArgs<Command>>? EndExecutingCommand;
		#endregion

		#region Fields
		Thread? _processThread;
		Thread? _publishThread;
		private bool _stopEngine;
		private int _unfinishedJobs;
		private volatile bool _executingJob;
		private volatile Command? _executingCommand;
		private volatile Job? _currentJob;
		private volatile int _currentCommandIndex;
		private volatile bool _abortCurrentJob;
		#endregion
	}
}
