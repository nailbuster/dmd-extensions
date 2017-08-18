using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using LibDmd.Common;
using LibDmd.Common.HeatShrink;
using LibDmd.Input.FileSystem;
using NLog;

namespace LibDmd.Converter.Colorize
{

	public class VniAnimationSet : AnimationSet
	{
		public VniAnimationSet(string filename)
		{
			var fs = new FileStream(filename, FileMode.Open);
			var reader = new BinaryReader(fs);

			// name
			var header = Encoding.UTF8.GetString(reader.ReadBytes(4));
			if (header != "VPIN") {
				throw new WrongFormatException("Not a VPIN file: " + filename);
			}

			// version
			Version = reader.ReadInt16BE();

			// number of animations
			var numAnimations = reader.ReadInt16BE();

			if (Version >= 2) {
				Logger.Trace("Skipping {0} bytes of animation indexes.", numAnimations * 4);
				for (var i = 0; i < numAnimations; i++) {
					reader.ReadUInt32();
				}
			}

			Animations = new List<Animation>(numAnimations);
			Logger.Debug("Reading {0} animations from {1} v{2}...", numAnimations, header, Version);
			for (var i = 0; i < numAnimations; i++) {
				Animations.Add(new VniAnimation(reader, Version));
			}
		}

		public override string ToString()
		{
			return $"VPIN v{Version}, {Animations.Count} animation(s)";
		}
	}

	public class VniAnimation : Animation
	{
		public VniAnimation(BinaryReader reader, int fileVersion)
		{
			// animations name
			var nameLength = reader.ReadInt16BE();
			Name = nameLength > 0 ? Encoding.UTF8.GetString(reader.ReadBytes(nameLength)) : "<undefined>";

			// other data
			Cycles = reader.ReadInt16BE();
			Hold = reader.ReadInt16BE();
			ClockFrom = reader.ReadInt16BE();
			ClockSmall = reader.ReadByte() != 0;
			ClockInFront = reader.ReadByte() != 0;
			ClockOffsetX = reader.ReadInt16BE();
			ClockOffsetY = reader.ReadInt16BE();
			RefreshDelay = reader.ReadInt16BE();
			Type = reader.ReadByte();
			Fsk = reader.ReadByte();

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
			if (fileVersion >= 4) {
				Width = reader.ReadInt16BE();
				Height = reader.ReadInt16BE();
			}

			Logger.Debug("Reading {0} frame{1} for animation \"{2}\"...", numFrames, numFrames == 1 ? "" : "s", Name);
			Frames = new List<AnimationFrame>(numFrames);
			for (var i = 0; i < numFrames; i++) {
				var frame = new VniAnimationFrame(reader, fileVersion);
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
				return;
			}
			AnimationColors = new Color[numColors];
			for (var i = 0; i < numColors; i++) {
				AnimationColors[i] = Color.FromRgb(reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
			}
			Logger.Debug("Found {0} colors for palette {1}.", numColors, PaletteIndex);
		}

		public override string ToString()
		{
			return $"{Name}, {Frames.Count} frames";
		}
	}

	public class VniAnimationFrame : AnimationFrame
	{
		public VniAnimationFrame(BinaryReader reader, int fileVersion)
		{
			int planeSize = reader.ReadInt16BE();
			Delay = reader.ReadInt16BE();
			if (fileVersion >= 4) {
				Hash = reader.ReadBytes(4);
			}
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
			//Logger.Debug("Reading {0} {1}planes at {2} bytes for frame...", numPlanes, compressed ? "compressed " : "", planeSize);
			AnimationPlane mask = null;
			for (var i = 0; i < numPlanes; i++) {
				var plane = new VniAnimationPlane(reader, planeSize);
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

	public class VniAnimationPlane : AnimationPlane
	{
		public VniAnimationPlane(BinaryReader reader, int planeSize)
		{
			Marker = reader.ReadByte();
			Plane = reader.ReadBytes(planeSize);
		}
	}
}
