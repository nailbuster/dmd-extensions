using System.Collections.Generic;
using System.IO;
using LibDmd.Common;
using LibDmd.Common.HeatShrink;
using MonoLibUsb;

namespace LibDmd.Converter.Colorize
{
	public class VniAnimationFrame : AnimationFrame
	{
		public VniAnimationFrame(BinaryReader reader, int fileVersion, uint time) : base(time)
		{
			int planeSize = reader.ReadInt16BE();
			Delay = (uint) reader.ReadInt16BE();
			if (fileVersion >= 4) {
				Hash = reader.ReadBytes(4);
			}
			BitLength = reader.ReadByte();
			Planes = new List<AnimationPlane>(BitLength);
			
			if (fileVersion < 3) {
				HasMask = ReadPlanes(reader, planeSize);

			} else {
				var compressed = reader.ReadByte() != 0;
				if (!compressed) {
					HasMask = ReadPlanes(reader, planeSize);

				} else {

					var compressedSize = reader.ReadInt32BE();
					var compressedPlanes = reader.ReadBytes(compressedSize);
					var dec = new HeatShrinkDecoder(10, 0, 1024);
					var decompressedStream = new MemoryStream();
					dec.Decode(new MemoryStream(compressedPlanes), decompressedStream);
					decompressedStream.Seek(0, SeekOrigin.Begin);
					HasMask = ReadPlanes(new BinaryReader(decompressedStream), planeSize);
				}
			}
		}

		private bool ReadPlanes(BinaryReader reader, int planeSize)
		{
			AnimationPlane mask = null;
			for (var i = 0; i < BitLength; i++) {
				var plane = new VniAnimationPlane(reader, planeSize);
				if (plane.Marker < BitLength) {
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