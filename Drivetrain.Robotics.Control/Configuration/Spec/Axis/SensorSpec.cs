using System;
using System.Linq;

namespace Drivetrain.Robotics.Control
{
	public abstract class SensorSpec : Spec
	{
		public SensorSpec(string label, int sensorNumber, int board, int port, int readHigh, int readLow, Coordinate location = null)
			: base(label)
		{
			ReadHigh = readHigh;
			ReadLow = readLow;
			Board = board;
			Port = port;
			SensorNumber = sensorNumber;
			Location = location;
		}

		/// <summary>The temperature (thermistor) input the sensor is connected to; its analog reading is taken from there (M308).</summary>
		public BoardPort BoardPort => BoardPort.Temp(Board, Port);
		/// <summary>The input's name in config.g, e.g. "temp0" or "1.temp0".</summary>
		public string? PortDesignation => BoardPort.Designation;
		public int Board { get; set; } 
		public int Port { get; set; }
		public int SensorNumber { get; set; }
		public int ReadHigh { get; set; }
		public int ReadLow { get; set; }
		public Coordinate? Location { get; set; }
	}
}
