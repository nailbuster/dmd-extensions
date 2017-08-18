using System.Collections.Generic;
using NLog;

namespace LibDmd.Converter.Colorize
{
	public abstract class AnimationFrame
	{
		/// <summary>
		/// Duration of the frame
		/// </summary>
		public int Delay { get; protected set; }
		public List<AnimationPlane> Planes { get; protected set; }
		public bool HasMask { get; protected set; }
		public byte[] Hash { get; protected set; }

		protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();
	}
}