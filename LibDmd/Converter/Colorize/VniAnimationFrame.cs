using System.Collections.Generic;
using System.IO;
using LibDmd.Common;
using LibDmd.Common.HeatShrink;

namespace LibDmd.Converter.Colorize
{
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
}