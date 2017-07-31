using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using LibDmd.Common;
using NLog;

namespace LibDmd.Converter.Colorize
{
	public class Animation
	{
		public readonly string Name;
		private readonly int _cycles;
		private readonly int _hold;
		private readonly int _clockFrom;
		private readonly int _clockSmall;
		private readonly int _clockInFront;
		private readonly int _clockOffsetX;
		private readonly int _clockOffsetY;
		private readonly int _refreshDelay;
		private readonly int _type;
		private readonly int _fskTag;

		public readonly List<AnimationFrame> Frames;

		protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		public Animation(BinaryReader reader)
		{
			// animations name
			var nameLength = reader.ReadInt16BE();
			Name = nameLength > 0 ? Encoding.UTF8.GetString(reader.ReadBytes(nameLength - 2)) : "<undefined>";

			// other data
			_cycles = reader.ReadInt16BE();
			_hold = reader.ReadInt16BE();
			_clockFrom = reader.ReadInt16BE();
			_clockSmall = reader.ReadByte();
			_clockInFront = reader.ReadByte();
			_clockOffsetX = reader.ReadInt16BE();
			_clockOffsetY = reader.ReadInt16BE();
			_refreshDelay = reader.ReadInt16BE();
			_type = reader.ReadByte();
			_fskTag = reader.ReadByte();

			int numFrames = reader.ReadInt16BE();
			Logger.Trace("Reading {0} frames...", numFrames);
			Frames = new List<AnimationFrame>(numFrames);
			for (var i = 0; i < numFrames; i++) {
				Frames.Add(new AnimationFrame(reader));
			}
		}

		public override string ToString()
		{
			return $"{Name}, {Frames.Count} frames";
		}
	}

	public class AnimationFrame
	{
		public readonly int Duration;
		public readonly List<AnimationPlane> Planes;

		public AnimationFrame(BinaryReader reader)
		{
			int planeSize = reader.ReadInt16BE();
			Duration = reader.ReadInt16BE();
			int numPlanes = reader.ReadByte();
			Planes = new List<AnimationPlane>(numPlanes);
			for (var i = 0; i < numPlanes; i++) {
				Planes.Add(new AnimationPlane(reader, planeSize));
			}
		}
	}

	public class AnimationPlane
	{
		public readonly byte Type;
		public readonly byte[] Plane;

		public AnimationPlane(BinaryReader reader, int planeSize)
		{
			Type = reader.ReadByte(); 
			Plane = reader.ReadBytes(planeSize);
		}
	}
}
