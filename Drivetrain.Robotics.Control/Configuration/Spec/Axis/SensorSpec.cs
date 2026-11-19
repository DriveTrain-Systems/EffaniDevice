using System;
using System.Linq;

namespace Drivetrain.Robotics.Control
{
	public abstract class SensorSpec : Spec
	{
		public SensorSpec(string label, int sensorNumber, int board, int port, int readHigh, int readLow, Coordinate location = null, PortKind portKind = PortKind.Temp)
			: base(label)
		{
			PortKind = portKind;
			ReadHigh = readHigh;
			ReadLow = readLow;
			Board = board;
			Port = port;
			SensorNumber = sensorNumber;
			Location = location;
		}

		/// <summary>
		/// The input the sensor is connected to, which its analog reading is taken from (M308): a temperature (thermistor) input, or
		/// the input side of an IO connector when <see cref="PortKind"/> is <see cref="PortKind.Input"/>.
		/// </summary>
		public BoardPort BoardPort => PortKind == PortKind.Input ? BoardPort.Input(Board, Port) : BoardPort.Temp(Board, Port);
		/// <summary>The input's name in config.g, e.g. "temp0", "1.temp0" or "io6.in".</summary>
		public string? PortDesignation => BoardPort.Designation;
		/// <summary>Whether the sensor is on a temperature input or on the input side of an IO connector.</summary>
		public PortKind PortKind { get; set; }
		public int Board { get; set; } 
		public int Port { get; set; }
		public int SensorNumber { get; set; }
		public int ReadHigh { get; set; }
		public int ReadLow { get; set; }
		public Coordinate? Location { get; set; }
	}
}
