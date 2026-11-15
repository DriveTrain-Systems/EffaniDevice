using System;
using System.Linq;

namespace Drivetrain.Robotics.Control
{
	public class Plate : Spec
	{
		public Plate(Coordinate index) : base(null)
		{
			Location = index;
			// a letter for the tray's position across the table (X) and a number for its row (Y): A1, B1 ... then A2, B2 ...
			var column = (int)index.X;
			var letter = column < 26 ? ((char)('A' + column)).ToString() : $"{column + 1}-";

			Label = $"{letter}{(int)index.Y + 1}";
		}

		public Coordinate Location { get; } // from left to right and bottom to top, this plate's position

		public List<Well> Wells { get; } = new();
	}
}
