using System.IO;

namespace LibDmd.Converter.Colorize
{
	public class VniAnimationPlane : AnimationPlane
	{
		public VniAnimationPlane(BinaryReader reader, int planeSize)
		{
			Marker = reader.ReadByte();
			Plane = reader.ReadBytes(planeSize);
		}
	}
}