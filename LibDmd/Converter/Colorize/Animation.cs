using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using LibDmd.Common;
using LibDmd.Common.HeatShrink;
using NLog;

namespace LibDmd.Converter.Colorize
{
	public class Animation
	{
		public readonly string Name;
		private readonly int _cycles;
		private readonly int _hold;
		private readonly int _clockFrom;
		private readonly bool _clockSmall;
		private readonly bool _clockInFront;
		private readonly int _clockOffsetX;
		private readonly int _clockOffsetY;
		private readonly int _refreshDelay;
		[Obsolete]
		private readonly int _type;
		private readonly int _fsk;

		public int PaletteIndex { get; private set; }
		public Color[] AnimationColors { get; private set; }
		public AnimationEditMode EditMode { get; private set; }
		public int TransitionFrom { get; }

		public readonly List<AnimationFrame> Frames;

		protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		public Animation(BinaryReader reader, int fileVersion)
		{
			// animations name
			var nameLength = reader.ReadInt16BE();
			Name = nameLength > 0 ? Encoding.UTF8.GetString(reader.ReadBytes(nameLength)) : "<undefined>";

			// other data
			_cycles = reader.ReadInt16BE();
			_hold = reader.ReadInt16BE();
			_clockFrom = reader.ReadInt16BE();
			_clockSmall = reader.ReadByte() != 0;
			_clockInFront = reader.ReadByte() != 0;
			_clockOffsetX = reader.ReadInt16BE();
			_clockOffsetY = reader.ReadInt16BE();
			_refreshDelay = reader.ReadInt16BE();
			_type = reader.ReadByte();
			_fsk = reader.ReadByte();

			int numFrames = reader.ReadInt16BE();
			if (numFrames < 0) {
				numFrames += 65536;
			}

			if (fileVersion >= 2) {
				ReadPalettesAndColors(reader);
			}
			if (fileVersion >= 3) {
				EditMode = (AnimationEditMode)reader.ReadByte();
			}

			Logger.Debug("Reading {0} frames for animation \"{1}\"...", numFrames, Name);
			Frames = new List<AnimationFrame>(numFrames);
			for (var i = 0; i < numFrames; i++) {
				var frame = new AnimationFrame(reader, fileVersion);
				if (frame.HasMask && TransitionFrom == 0) {
					TransitionFrom = i;
				}
				Frames.Add(frame);
			}
		}

		private void ReadPalettesAndColors(BinaryReader reader)
		{
			PaletteIndex = reader.ReadInt16BE();
			var numColors = reader.ReadInt16BE();
			if (numColors <= 0) {
				Logger.Debug("No colors for palette {0} found ({1}).", PaletteIndex, numColors);
				return;
			}
			AnimationColors = new Color[numColors];
			for (var i = 0; i < numColors; i++) {
				AnimationColors[i] = Color.FromRgb(reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
			}
			Logger.Debug("Found {1} colors for palette {0} found.", numColors, PaletteIndex);
		}

		public override string ToString()
		{
			return $"{Name}, {Frames.Count} frames";
		}
	}

	public enum AnimationEditMode
	{
		Replace, Mask, Fixed
	}

	public class AnimationFrame
	{
		/// <summary>
		/// Duration of the frame
		/// </summary>
		public readonly int Delay;
		public readonly List<AnimationPlane> Planes;
		public readonly bool HasMask;

		protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		public AnimationFrame(BinaryReader reader, int fileVersion)
		{
			int planeSize = reader.ReadInt16BE();
			Delay = reader.ReadInt16BE();
			int numPlanes = reader.ReadByte();
			Planes = new List<AnimationPlane>(numPlanes);
			if (fileVersion < 3) {
				HasMask = ReadPlanes(reader, numPlanes, planeSize, false);

			} else {
				var compressed = reader.ReadByte() != 0;
				if (!compressed) {
					HasMask = ReadPlanes(reader, numPlanes, planeSize, false);

				} else {

					var compressedSize = reader.ReadInt32BE();
					var compressedPlanes = reader.ReadBytes(compressedSize);
					var dec = new HeatShrinkDecoder(10, 0, 1024);
					var decompressedStream = new MemoryStream();
					dec.Decode(new MemoryStream(compressedPlanes), decompressedStream);
					decompressedStream.Seek(0, SeekOrigin.Begin);
					HasMask = ReadPlanes(new BinaryReader(decompressedStream), numPlanes, planeSize, true);
				}
			}
		}

		private bool ReadPlanes(BinaryReader reader, int numPlanes, int planeSize, bool compressed)
		{
			Logger.Debug("Reading {0} {1}planes at {2} bytes for frame...", numPlanes, compressed ? "compressed " : "", planeSize);
			AnimationPlane mask = null;
			for (var i = 0; i < numPlanes; i++) {
				var plane = new AnimationPlane(reader, planeSize);
				if (plane.Marker < numPlanes) {
					Planes.Add(plane);
				} else {
					mask = plane;
				}
			}
			// mask plane is the last in list but first in file
			if (mask == null) {
				return false;
			}
			Planes.Add(mask);
			return true;
		}
	}

	public class AnimationPlane
	{
		/// <summary>
		/// Type of plane
		/// </summary>
		public readonly byte Marker;
		public readonly byte[] Plane;

		public AnimationPlane(BinaryReader reader, int planeSize)
		{
			Marker = reader.ReadByte();
			Plane = reader.ReadBytes(planeSize);
		}
	}
}
